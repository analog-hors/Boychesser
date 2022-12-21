import torch

from dataloader import Batch, InputFeatureSet, BucketingScheme


def get_tensors(batch: Batch, feature_count: int) -> list[torch.Tensor]:
    tensors = []
    for indices, values in zip(batch.indices, batch.values):
        t = indices.reshape(-1, 2).T
        tensors.append(torch.sparse_coo_tensor(
            t, values, (batch.size, feature_count)
        ).to_dense())
    return tensors


FEATURES = (64+32+32+32+32+64+64)*2
class Ice4Model(torch.nn.Module):
    def __init__(self):
        super().__init__()
        self.params = torch.nn.Linear(FEATURES, 1, bias=False)
        self.bucketing_scheme = BucketingScheme.NO_BUCKETING

    def forward(self, batch: Batch):
        features, = get_tensors(batch, FEATURES)

        result = self.params(features)

        return torch.sigmoid(result)

    def input_feature_set(self) -> InputFeatureSet:
        return InputFeatureSet.ICE4_FEATURES


class NnBoard768(torch.nn.Module):
    def __init__(self, ft_out: int, bucketing_scheme: BucketingScheme):
        super().__init__()
        self.bucketing_scheme = bucketing_scheme
        self.bucket_count = bucketing_scheme.bucket_count()
        self.ft = torch.nn.Linear(768, ft_out)
        self.out = torch.nn.Linear(ft_out * 2, 1 * self.bucket_count)
        self.idx_cache = None

    def forward(self, batch: Batch):
        stm_indices = batch.stm_indices.reshape(-1, 2).T
        nstm_indices = batch.nstm_indices.reshape(-1, 2).T
        board_stm_sparse = torch.sparse_coo_tensor(
            stm_indices, batch.values, (batch.size, 768)
        ).to_dense()
        board_nstm_sparse = torch.sparse_coo_tensor(
            nstm_indices, batch.values, (batch.size, 768)
        ).to_dense()

        stm_ft = self.ft(board_stm_sparse)
        nstm_ft = self.ft(board_nstm_sparse)

        hidden = torch.clamp(torch.cat((stm_ft, nstm_ft), dim=1), 0, 1)

        if self.idx_cache is None or self.idx_cache.shape[0] != hidden.shape[0]:
            self.idx_cache = torch.arange(
                0, hidden.shape[0] * self.bucket_count, self.bucket_count,
                device=batch.buckets.device
            )
        indices = batch.buckets.flatten() + self.idx_cache

        l1_out = self.out(hidden).view(-1, 1)[indices]

        return torch.sigmoid(l1_out)

    def input_feature_set(self) -> InputFeatureSet:
        return InputFeatureSet.BOARD_768


class NnHalfKP(torch.nn.Module):
    def __init__(self, ft_out: int, bucketing_scheme: BucketingScheme):
        super().__init__()
        self.bucketing_scheme = bucketing_scheme
        self.bucket_count = bucketing_scheme.bucket_count()
        self.ft = torch.nn.Linear(40960, ft_out)
        self.fft = torch.nn.Linear(640, ft_out)
        self.out = torch.nn.Linear(ft_out * 2, 1)
        self.idx_cache = None

    def forward(self, batch: Batch):

        stm_indices = batch.stm_indices.reshape(-1, 2).T
        nstm_indices = batch.nstm_indices.reshape(-1, 2).T
        board_stm_sparse = torch.sparse_coo_tensor(
            stm_indices, batch.values, (batch.size, 40960)
        )
        board_nstm_sparse = torch.sparse_coo_tensor(
            nstm_indices, batch.values, (batch.size, 40960)
        )

        v_stm_indices = torch.clone(stm_indices)
        v_nstm_indices = torch.clone(nstm_indices)
        v_stm_indices[1][:] %= 640
        v_nstm_indices[1][:] %= 640
        v_board_stm_sparse = torch.sparse_coo_tensor(
            v_stm_indices, batch.values, (batch.size, 640)
        ).to_dense()
        v_board_nstm_sparse = torch.sparse_coo_tensor(
            v_nstm_indices, batch.values, (batch.size, 640)
        ).to_dense()

        stm_ft = self.ft(board_stm_sparse) + self.fft(v_board_stm_sparse)
        nstm_ft = self.ft(board_nstm_sparse) + self.fft(v_board_nstm_sparse)

        hidden = torch.clamp(torch.cat((stm_ft, nstm_ft), dim=1), 0, 1)

        if self.idx_cache is None or self.idx_cache.shape[0] != hidden.shape[0]:
            self.idx_cache = torch.arange(
                0, hidden.shape[0] * self.bucket_count, self.bucket_count,
                device=batch.buckets.device
            )
        indices = batch.buckets.flatten() + self.idx_cache

        l1_out = self.out(hidden).view(-1, 1)[indices]

        return torch.sigmoid(l1_out)

    def input_feature_set(self) -> InputFeatureSet:
        return InputFeatureSet.HALF_KP


class NnHalfKA(torch.nn.Module):
    def __init__(self, ft_out: int, bucketing_scheme: BucketingScheme):
        super().__init__()
        self.bucketing_scheme = bucketing_scheme
        self.bucket_count = bucketing_scheme.bucket_count()
        self.ft = torch.nn.Linear(49152, ft_out)
        self.fft = torch.nn.Linear(768, ft_out)
        self.out = torch.nn.Linear(ft_out * 2, 1)
        self.idx_cache = None

    def forward(self, batch: Batch):
        stm_indices = batch.stm_indices.reshape(-1, 2).T
        nstm_indices = batch.nstm_indices.reshape(-1, 2).T
        board_stm_sparse = torch.sparse_coo_tensor(
            stm_indices, batch.values, (batch.size, 49152)
        )
        board_nstm_sparse = torch.sparse_coo_tensor(
            nstm_indices, batch.values, (batch.size, 49152)
        )

        v_stm_indices = torch.clone(stm_indices)
        v_nstm_indices = torch.clone(nstm_indices)
        v_stm_indices[1][:] %= 768
        v_nstm_indices[1][:] %= 768
        v_board_stm_sparse = torch.sparse_coo_tensor(
            v_stm_indices, batch.values, (batch.size, 768)
        ).to_dense()
        v_board_nstm_sparse = torch.sparse_coo_tensor(
            v_nstm_indices, batch.values, (batch.size, 768)
        ).to_dense()

        stm_ft = self.ft(board_stm_sparse) + self.fft(v_board_stm_sparse)
        nstm_ft = self.ft(board_nstm_sparse) + self.fft(v_board_nstm_sparse)

        hidden = torch.clamp(torch.cat((stm_ft, nstm_ft), dim=1), 0, 1)

        if self.idx_cache is None or self.idx_cache.shape[0] != hidden.shape[0]:
            self.idx_cache = torch.arange(
                0, hidden.shape[0] * self.bucket_count, self.bucket_count,
                device=batch.buckets.device
            )
        indices = batch.buckets.flatten() + self.idx_cache

        l1_out = self.out(hidden).view(-1, 1)[indices]

        return torch.sigmoid(l1_out)

    def input_feature_set(self) -> InputFeatureSet:
        return InputFeatureSet.HALF_KA


class NnBoard768Cuda(torch.nn.Module):
    def __init__(self, ft_out: int, bucketing_scheme: BucketingScheme):
        from cudasparse import DoubleFeatureTransformerSlice

        super().__init__()
        self.bucketing_scheme = bucketing_scheme
        self.bucket_count = bucketing_scheme.bucket_count()
        self.max_features = InputFeatureSet.BOARD_768_CUDA.max_features()
        self.ft = DoubleFeatureTransformerSlice(768, ft_out)
        self.out = torch.nn.Linear(ft_out * 2, 1)
        self.idx_cache = None

    def forward(self, batch: Batch):
        values = batch.values.reshape(-1, self.max_features)
        stm_indices = batch.stm_indices.reshape(-1, self.max_features).type(
            dtype=torch.int32
        )
        nstm_indices = batch.nstm_indices.reshape(-1, self.max_features).type(
            dtype=torch.int32
        )
        stm_ft, nstm_ft = self.ft(
            stm_indices,
            values,
            nstm_indices,
            values,
        )

        hidden = torch.clamp(torch.cat((stm_ft, nstm_ft), dim=1), 0, 1)

        if self.idx_cache is None or self.idx_cache.shape[0] != hidden.shape[0]:
            self.idx_cache = torch.arange(
                0, hidden.shape[0] * self.bucket_count, self.bucket_count,
                device=batch.buckets.device
            )
        indices = batch.buckets.flatten() + self.idx_cache

        l1_out = self.out(hidden).view(-1, 1)[indices]

        return torch.sigmoid(l1_out)

    def input_feature_set(self) -> InputFeatureSet:
        return InputFeatureSet.BOARD_768_CUDA


class NnHalfKPCuda(torch.nn.Module):
    def __init__(self, ft_out: int, bucketing_scheme: BucketingScheme):
        from cudasparse import DoubleFeatureTransformerSlice

        super().__init__()
        self.bucketing_scheme = bucketing_scheme
        self.bucket_count = bucketing_scheme.bucket_count()
        self.max_features = InputFeatureSet.HALF_KP_CUDA.max_features()
        self.ft = DoubleFeatureTransformerSlice(40960, ft_out)
        self.fft = DoubleFeatureTransformerSlice(640, ft_out)
        self.out = torch.nn.Linear(ft_out * 2, 1)
        self.idx_cache = None

    def forward(self, batch: Batch):
        values = batch.values.reshape(-1, self.max_features)
        stm_indices = batch.stm_indices.reshape(-1, self.max_features).type(
            dtype=torch.int32
        )
        nstm_indices = batch.nstm_indices.reshape(-1, self.max_features).type(
            dtype=torch.int32
        )
        stm_ft, nstm_ft = self.ft(
            stm_indices,
            values,
            nstm_indices,
            values,
        )
        v_stm_ft, v_nstm_ft = self.fft(
            stm_indices.fmod(640), values, nstm_indices.fmod(640), values
        )

        hidden = torch.clamp(
            torch.cat((stm_ft + v_stm_ft, nstm_ft + v_nstm_ft), dim=1), 0, 1
        )

        if self.idx_cache is None or self.idx_cache.shape[0] != hidden.shape[0]:
            self.idx_cache = torch.arange(
                0, hidden.shape[0] * self.bucket_count, self.bucket_count,
                device=batch.buckets.device
            )
        indices = batch.buckets.flatten() + self.idx_cache

        l1_out = self.out(hidden).view(-1, 1)[indices]

        return torch.sigmoid(l1_out)

    def input_feature_set(self) -> InputFeatureSet:
        return InputFeatureSet.HALF_KP_CUDA


class NnHalfKACuda(torch.nn.Module):
    def __init__(self, ft_out: int, bucketing_scheme: BucketingScheme):
        from cudasparse import DoubleFeatureTransformerSlice

        super().__init__()
        self.bucketing_scheme = bucketing_scheme
        self.bucket_count = bucketing_scheme.bucket_count()
        self.max_features = InputFeatureSet.HALF_KA_CUDA.max_features()
        self.ft = DoubleFeatureTransformerSlice(49152, ft_out)
        self.fft = DoubleFeatureTransformerSlice(768, ft_out)
        self.out = torch.nn.Linear(ft_out * 2, 1)
        self.idx_cache = None

    def forward(self, batch: Batch):
        values = batch.values.reshape(-1, self.max_features)
        stm_indices = batch.stm_indices.reshape(-1, self.max_features).type(
            dtype=torch.int32
        )
        nstm_indices = batch.nstm_indices.reshape(-1, self.max_features).type(
            dtype=torch.int32
        )
        stm_ft, nstm_ft = self.ft(
            stm_indices,
            values,
            nstm_indices,
            values,
        )
        v_stm_ft, v_nstm_ft = self.fft(
            stm_indices.fmod(768), values, nstm_indices.fmod(768), values
        )

        hidden = torch.clamp(
            torch.cat((stm_ft + v_stm_ft, nstm_ft + v_nstm_ft), dim=1), 0, 1
        )

        if self.idx_cache is None or self.idx_cache.shape[0] != hidden.shape[0]:
            self.idx_cache = torch.arange(
                0, hidden.shape[0] * self.bucket_count, self.bucket_count,
                device=batch.buckets.device
            )
        indices = batch.buckets.flatten() + self.idx_cache

        l1_out = self.out(hidden).view(-1, 1)[indices]

        return torch.sigmoid(l1_out)

    def input_feature_set(self) -> InputFeatureSet:
        return InputFeatureSet.HALF_KA_CUDA

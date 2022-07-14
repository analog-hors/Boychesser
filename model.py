import torch
from cudasparse import DoubleFeatureTransformerSlice

from dataloader import Batch, InputFeatureSet


class NnBoard768(torch.nn.Module):
    def __init__(self, ft_out: int):
        super().__init__()
        self.ft = torch.nn.Linear(768, ft_out)
        self.out = torch.nn.Linear(ft_out * 2, 1)

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

        return torch.sigmoid(self.out(hidden))

    def input_feature_set(self) -> InputFeatureSet:
        return InputFeatureSet.BOARD_768


class NnHalfKP(torch.nn.Module):
    def __init__(self, ft_out: int):
        super().__init__()
        self.ft = torch.nn.Linear(40960, ft_out)
        self.fft = torch.nn.Linear(640, ft_out)
        self.out = torch.nn.Linear(ft_out * 2, 1)

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

        return torch.sigmoid(self.out(hidden))

    def input_feature_set(self) -> InputFeatureSet:
        return InputFeatureSet.HALF_KP


class NnHalfKA(torch.nn.Module):
    def __init__(self, ft_out: int):
        super().__init__()
        self.ft = torch.nn.Linear(49152, ft_out)
        self.fft = torch.nn.Linear(768, ft_out)
        self.out = torch.nn.Linear(ft_out * 2, 1)

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

        return torch.sigmoid(self.out(hidden))

    def input_feature_set(self) -> InputFeatureSet:
        return InputFeatureSet.HALF_KA


class NnBoard768Cuda(torch.nn.Module):
    def __init__(self, ft_out: int):
        super().__init__()
        self.max_features = InputFeatureSet.BOARD_768_CUDA.max_features()
        self.ft = DoubleFeatureTransformerSlice(768, ft_out)
        self.out = torch.nn.Linear(ft_out * 2, 1)

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

        return torch.sigmoid(self.out(hidden))

    def input_feature_set(self) -> InputFeatureSet:
        return InputFeatureSet.BOARD_768_CUDA


class NnHalfKPCuda(torch.nn.Module):
    def __init__(self, ft_out: int):
        super().__init__()
        self.max_features = InputFeatureSet.HALF_KP_CUDA.max_features()
        self.ft = DoubleFeatureTransformerSlice(40960, ft_out)
        self.fft = DoubleFeatureTransformerSlice(640, ft_out)
        self.out = torch.nn.Linear(ft_out * 2, 1)

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
            stm_indices % 640, values, nstm_indices % 640, values
        )

        hidden = torch.clamp(
            torch.cat((stm_ft + v_stm_ft, nstm_ft + v_nstm_ft), dim=1), 0, 1
        )

        return torch.sigmoid(self.out(hidden))

    def input_feature_set(self) -> InputFeatureSet:
        return InputFeatureSet.HALF_KP_CUDA


class NnHalfKACuda(torch.nn.Module):
    def __init__(self, ft_out: int):
        super().__init__()
        self.max_features = InputFeatureSet.HALF_KA_CUDA.max_features()
        self.ft = DoubleFeatureTransformerSlice(49152, ft_out)
        self.fft = DoubleFeatureTransformerSlice(768, ft_out)
        self.out = torch.nn.Linear(ft_out * 2, 1)

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
            stm_indices % 768, values, nstm_indices % 768, values
        )

        hidden = torch.clamp(
            torch.cat((stm_ft + v_stm_ft, nstm_ft + v_nstm_ft), dim=1), 0, 1
        )

        return torch.sigmoid(self.out(hidden))

    def input_feature_set(self) -> InputFeatureSet:
        return InputFeatureSet.HALF_KA_CUDA

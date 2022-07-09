import torch

from dataloader import Batch


class NnBoard768(torch.nn.Module):
    def __init__(self, ft_out: int):
        super().__init__()
        self.ft = torch.nn.Linear(768, ft_out)
        self.out = torch.nn.Linear(ft_out * 2, 1)

    def forward(self, batch: Batch):
        board_stm_sparse = torch.sparse_coo_tensor(
            batch.stm_indices, batch.values, (batch.size, 768)
        )
        board_nstm_sparse = torch.sparse_coo_tensor(
            batch.nstm_indices, batch.values, (batch.size, 768)
        )

        stm_ft = self.ft(board_stm_sparse)
        nstm_ft = self.ft(board_nstm_sparse)

        hidden = torch.clamp(torch.cat((stm_ft, nstm_ft), dim=1), 0, 1)

        return torch.sigmoid(self.out(hidden))


class NnHalfKP(torch.nn.Module):
    def __init__(self, ft_out: int):
        super().__init__()
        self.ft = torch.nn.Linear(40960, ft_out)
        self.fft = torch.nn.Linear(640, ft_out)
        self.out = torch.nn.Linear(ft_out * 2, 1)

    def forward(self, batch: Batch):

        board_stm_sparse = torch.sparse_coo_tensor(
            batch.stm_indices, batch.values, (batch.size, 40960)
        )
        board_nstm_sparse = torch.sparse_coo_tensor(
            batch.nstm_indices, batch.values, (batch.size, 40960)
        )

        batch.stm_indices[1][:] %= 640
        batch.nstm_indices[1][:] %= 640
        v_board_stm_sparse = torch.sparse_coo_tensor(
            batch.stm_indices, batch.values, (batch.size, 640)
        )
        v_board_nstm_sparse = torch.sparse_coo_tensor(
            batch.nstm_indices, batch.values, (batch.size, 640)
        )

        stm_ft = self.ft(board_stm_sparse) + self.fft(v_board_stm_sparse)
        nstm_ft = self.ft(board_nstm_sparse) + self.fft(v_board_nstm_sparse)

        hidden = torch.clamp(torch.cat((stm_ft, nstm_ft), dim=1), 0, 1)

        return torch.sigmoid(self.out(hidden))


class NnHalfKA(torch.nn.Module):
    def __init__(self, ft_out: int):
        super().__init__()
        self.ft = torch.nn.Linear(49152, ft_out)
        self.fft = torch.nn.Linear(768, ft_out)
        self.out = torch.nn.Linear(ft_out * 2, 1)

    def forward(self, batch: Batch):

        board_stm_sparse = torch.sparse_coo_tensor(
            batch.stm_indices, batch.values, (batch.size, 49152)
        )
        board_nstm_sparse = torch.sparse_coo_tensor(
            batch.nstm_indices, batch.values, (batch.size, 49152)
        )

        batch.stm_indices[1][:] %= 768
        batch.nstm_indices[1][:] %= 768
        v_board_stm_sparse = torch.sparse_coo_tensor(
            batch.stm_indices, batch.values, (batch.size, 768)
        )
        v_board_nstm_sparse = torch.sparse_coo_tensor(
            batch.nstm_indices, batch.values, (batch.size, 768)
        )

        stm_ft = self.ft(board_stm_sparse) + self.fft(v_board_stm_sparse)
        nstm_ft = self.ft(board_nstm_sparse) + self.fft(v_board_nstm_sparse)

        hidden = torch.clamp(torch.cat((stm_ft, nstm_ft), dim=1), 0, 1)

        return torch.sigmoid(self.out(hidden))

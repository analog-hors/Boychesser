import torch


class Nn(torch.nn.Module):
    def __init__(self, ft_out: int):
        super().__init__()
        self.ft = torch.nn.Linear(40960, ft_out)
        self.fft = torch.nn.Linear(640, ft_out)
        self.out = torch.nn.Linear(ft_out * 2, 1)

    def forward(self, boards):

        stm_ft = self.ft(boards[0]) + self.fft(boards[2])
        nstm_ft = self.ft(boards[1]) + self.fft(boards[3])

        hidden = torch.clamp(torch.cat((stm_ft, nstm_ft), dim=1), 0, 1)

        return torch.sigmoid(self.out(hidden))

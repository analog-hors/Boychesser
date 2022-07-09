import ctypes
from dataclasses import dataclass
from glob import glob
from typing import List, Union

import numpy as np
import torch


def locate_dynamic_lib() -> str:
    files = glob("./libparse.*")
    return files[0]


@dataclass
class Batch:
    boards_stm: torch.Tensor
    boards_nstm: torch.Tensor
    v_boards_stm: torch.Tensor
    v_boards_nstm: torch.Tensor
    cp: torch.Tensor
    wdl: torch.Tensor


class BatchLoader:
    def __init__(self, batch_size: int, device: torch.device):
        lib = ctypes.cdll.LoadLibrary(locate_dynamic_lib())
        new_batch_loader = lib.new_batch_loader
        new_batch_loader.restype = ctypes.POINTER(ctypes.c_uint64)

        self.batch_size = batch_size
        self.batch_loader = new_batch_loader(ctypes.c_int32(batch_size))

        self.open_file = lib.open_file
        self.open_file.restype = ctypes.c_bool
        self.close_file = lib.close_file
        self.read_batch = lib.read_batch
        self.read_batch.restype = ctypes.c_bool

        self.boards_stm = lib.boards_stm
        self.boards_stm.restype = ctypes.POINTER(ctypes.c_int64)

        self.boards_nstm = lib.boards_nstm
        self.boards_nstm.restype = ctypes.POINTER(ctypes.c_int64)

        self.v_boards_stm = lib.v_boards_stm
        self.v_boards_stm.restype = ctypes.POINTER(ctypes.c_int64)

        self.v_boards_nstm = lib.v_boards_nstm
        self.v_boards_nstm.restype = ctypes.POINTER(ctypes.c_int64)

        self.values = lib.values
        self.values.restype = ctypes.POINTER(ctypes.c_float)

        self.count = lib.count
        self.count.restype = ctypes.c_uint32

        self.cp = lib.cp
        self.cp.restype = ctypes.POINTER(ctypes.c_float)

        self.wdl = lib.wdl
        self.wdl.restype = ctypes.POINTER(ctypes.c_float)

        self.files: List[str] = []
        self.curr_file: Union[int, None] = 0
        self.device = device

    def to_pytorch(self, array: np.ndarray) -> torch.Tensor:
        tch_array = torch.from_numpy(array)
        if torch.cuda.is_available():
            tch_array = tch_array.pin_memory()
        return torch.from_numpy(array).to(self.device, non_blocking=True)

    def set_directory(self, directory: str):
        self.set_files(glob(f"{directory}/*.txt"))

    def add_directory(self, directory: str):
        self.set_files(glob(f"{directory}/*.txt"))

    def set_files(self, files: List[str]):
        self._close()
        self.files = files
        self.curr_file = None

    def add_files(self, files: List[str]):
        self._close()
        self.files += files
        self.curr_file = None

    def _open(self, path: str) -> bool:
        as_bytes = bytes(path, "utf-8")
        return self.open_file(self.batch_loader, ctypes.create_string_buffer(as_bytes))

    def _close(self):
        self.close_file(self.batch_loader)

    def _read_next_batch(self) -> bool:
        if self.curr_file is None:
            self.curr_file = 0
            self._open(self.files[self.curr_file])

        if self.read_batch(self.batch_loader):
            return True
        else:
            self._close()
            self.curr_file += 1
            self.curr_file %= len(self.files)
            self._open(self.files[self.curr_file])
            return self.read_batch(self.batch_loader)

    def get_next_batch(self) -> Union[None, Batch]:
        if not self._read_next_batch():
            return None
        count = self.count(self.batch_loader)
        boards_stm = self.to_pytorch(
            np.ctypeslib.as_array(
                self.boards_stm(self.batch_loader), shape=(count, 2)
            ).T
        )
        boards_nstm = self.to_pytorch(
            np.ctypeslib.as_array(
                self.boards_nstm(self.batch_loader), shape=(count, 2)
            ).T
        )

        v_boards_stm = self.to_pytorch(
            np.ctypeslib.as_array(
                self.v_boards_stm(self.batch_loader), shape=(count, 2)
            ).T
        )
        v_boards_nstm = self.to_pytorch(
            np.ctypeslib.as_array(
                self.v_boards_nstm(self.batch_loader), shape=(count, 2)
            ).T
        )

        values = self.to_pytorch(
            np.ctypeslib.as_array(self.values(self.batch_loader), shape=(count,)).T
        )

        board_stm_sparse = torch.sparse_coo_tensor(
            boards_stm, values, (self.batch_size, 40960)
        )
        board_nstm_sparse = torch.sparse_coo_tensor(
            boards_nstm, values, (self.batch_size, 40960)
        )

        v_board_stm_sparse = torch.sparse_coo_tensor(
            v_boards_stm, values, (self.batch_size, 640)
        )
        v_board_nstm_sparse = torch.sparse_coo_tensor(
            v_boards_nstm, values, (self.batch_size, 640)
        )

        cp = self.to_pytorch(
            np.ctypeslib.as_array(
                self.cp(self.batch_loader), shape=(self.batch_size, 1)
            )
        )
        wdl = self.to_pytorch(
            np.ctypeslib.as_array(
                self.wdl(self.batch_loader), shape=(self.batch_size, 1)
            )
        )

        return Batch(
            board_stm_sparse,
            board_nstm_sparse,
            v_board_stm_sparse,
            v_board_nstm_sparse,
            cp,
            wdl,
        )

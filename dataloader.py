import ctypes
from dataclasses import dataclass
from glob import glob
from typing import List, Union

import numpy as np


def locate_dynamic_lib() -> str:
    files = glob("./libparse.*")
    return files[0]


@dataclass
class Batch:
    boards_stm: np.array
    boards_nstm: np.array
    cp: np.array
    wdl: np.array


class BatchLoader:
    def __init__(self, batch_size: int):
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
        self.boards_stm.restype = ctypes.POINTER(ctypes.c_float)

        self.boards_nstm = lib.boards_nstm
        self.boards_nstm.restype = ctypes.POINTER(ctypes.c_float)

        self.cp = lib.cp
        self.cp.restype = ctypes.POINTER(ctypes.c_float)

        self.wdl = lib.wdl
        self.wdl.restype = ctypes.POINTER(ctypes.c_float)

        self.files = []
        self.curr_file = 0

    def set_directory(self, directory: str):
        self.set_files(glob(f"{directory}/*.txt"))

    def set_files(self, files: List[str]):
        self.__close()
        self.files = files
        self.curr_file = None

    def __open(self, path: str) -> bool:
        as_bytes = bytes(path, "utf-8")
        return self.open_file(self.batch_loader, ctypes.create_string_buffer(as_bytes))

    def __close(self):
        self.close_file(self.batch_loader)

    def __read_next_batch(self) -> bool:
        if self.curr_file is None:
            self.curr_file = 0
            self.__open(self.files[self.curr_file])

        if self.read_batch(self.batch_loader):
            return True
        else:
            self.__close()
            self.curr_file += 1
            self.curr_file %= len(self.files)
            self.__open(self.files[self.curr_file])
            return self.read_batch(self.batch_loader)

    def get_next_batch(self) -> Union[None, Batch]:
        if not self.__read_next_batch():
            return None

        boards_stm = np.ctypeslib.as_array(
            self.boards_stm(self.batch_loader), shape=(self.batch_size, 768)
        )
        boards_nstm = np.ctypeslib.as_array(
            self.boards_nstm(self.batch_loader), shape=(self.batch_size, 768)
        )
        cp = np.ctypeslib.as_array(
            self.cp(self.batch_loader), shape=(self.batch_size, 1)
        )
        wdl = np.ctypeslib.as_array(
            self.wdl(self.batch_loader), shape=(self.batch_size, 1)
        )

        return Batch(boards_stm, boards_nstm, cp, wdl)

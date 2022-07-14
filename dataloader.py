from __future__ import annotations

from dataclasses import dataclass
from enum import IntEnum

import ctypes
import os

import numpy as np
import torch


def _load_parse_lib():
    path = "./libparse.dll" if os.name == "nt" else "./libparse.so"
    lib = ctypes.cdll.LoadLibrary(path)

    lib.batch_new.restype = ctypes.c_void_p
    lib.batch_drop.restype = None
    lib.batch_get_capacity.restype = ctypes.c_uint32
    lib.batch_get_len.restype = ctypes.c_uint32
    lib.batch_get_stm_feature_buffer_ptr.restype = ctypes.POINTER(ctypes.c_int64)
    lib.batch_get_nstm_feature_buffer_ptr.restype = ctypes.POINTER(ctypes.c_int64)
    lib.batch_get_values_ptr.restype = ctypes.POINTER(ctypes.c_float)
    lib.batch_get_total_features.restype = ctypes.c_uint32
    lib.batch_get_cp_ptr.restype = ctypes.POINTER(ctypes.c_float)
    lib.batch_get_wdl_ptr.restype = ctypes.POINTER(ctypes.c_float)

    lib.file_reader_new.restype = ctypes.c_void_p
    lib.file_reader_drop.restype = None

    lib.input_feature_set_get_max_indices.restype = ctypes.c_uint32

    lib.read_batch_into.restype = ctypes.c_bool

    return lib


PARSE_LIB = _load_parse_lib()


class InputFeatureSet(IntEnum):
    BOARD_768 = 0
    HALF_KP = 1
    HALF_KA = 2

    def max_indices(self) -> int:
        return PARSE_LIB.input_feature_set_get_max_indices(self)


@dataclass
class Batch:
    stm_indices: torch.Tensor
    nstm_indices: torch.Tensor
    values: torch.Tensor
    cp: torch.Tensor
    wdl: torch.Tensor
    size: int


class ParserBatch:
    def __init__(self, batch_size: int, max_indices: int) -> None:
        self._ptr = ctypes.c_void_p(
            PARSE_LIB.batch_new(
                ctypes.c_uint32(batch_size), ctypes.c_uint32(max_indices)
            )
        )
        if self._ptr.value is None:
            raise Exception("Failed to create batch")

    def drop(self) -> None:
        if self._ptr.value is not None:
            PARSE_LIB.batch_drop(self._ptr)
            self._ptr.value = None

    def __enter__(self) -> ParserBatch:
        return self

    def __exit__(self) -> None:
        self.drop()

    def get_capacity(self) -> int:
        return PARSE_LIB.batch_get_capacity(self._ptr)

    def get_len(self) -> int:
        return PARSE_LIB.batch_get_len(self._ptr)

    def get_stm_feature_buffer_ptr(self) -> ctypes.pointer[ctypes.c_float]:
        return PARSE_LIB.batch_get_stm_feature_buffer_ptr(self._ptr)

    def get_nstm_feature_buffer_ptr(self) -> ctypes.pointer[ctypes.c_float]:
        return PARSE_LIB.batch_get_nstm_feature_buffer_ptr(self._ptr)

    def get_values_ptr(self) -> ctypes.pointer[ctypes.c_float]:
        return PARSE_LIB.batch_get_values_ptr(self._ptr)

    def get_total_features(self) -> int:
        return PARSE_LIB.batch_get_total_features(self._ptr)

    def get_cp_ptr(self) -> ctypes.pointer[ctypes.c_float]:
        return PARSE_LIB.batch_get_cp_ptr(self._ptr)

    def get_wdl_ptr(self) -> ctypes.pointer[ctypes.c_float]:
        return PARSE_LIB.batch_get_wdl_ptr(self._ptr)

    def to_pytorch_batch(self, device: torch.device) -> Batch:
        def to_pytorch(array: np.ndarray) -> torch.Tensor:
            tch_array = torch.from_numpy(array)
            if torch.cuda.is_available():
                tch_array = tch_array.pin_memory()
            return tch_array.to(device, non_blocking=True)

        total_features = self.get_total_features()
        boards_stm = to_pytorch(
            np.ctypeslib.as_array(
                self.get_stm_feature_buffer_ptr(), shape=(total_features, 2)
            ).T
        )
        boards_nstm = to_pytorch(
            np.ctypeslib.as_array(
                self.get_nstm_feature_buffer_ptr(), shape=(total_features, 2)
            ).T
        )
        values = to_pytorch(
            np.ctypeslib.as_array(self.get_values_ptr(), shape=(total_features,))
        )

        batch_len = self.get_len()
        cp = to_pytorch(np.ctypeslib.as_array(self.get_cp_ptr(), shape=(batch_len, 1)))
        wdl = to_pytorch(
            np.ctypeslib.as_array(self.get_wdl_ptr(), shape=(batch_len, 1))
        )

        return Batch(boards_stm, boards_nstm, values, cp, wdl, batch_len)


class ParserFileReader:
    def __init__(self, path: str) -> None:
        self._ptr = ctypes.c_void_p(
            PARSE_LIB.file_reader_new(ctypes.create_string_buffer(bytes(path, "ascii")))
        )
        if self._ptr.value is None:
            raise Exception("Failed to create file reader")

    def drop(self) -> None:
        if self._ptr.value is not None:
            PARSE_LIB.file_reader_drop(self._ptr)
            self._ptr.value = None

    def __enter__(self) -> ParserFileReader:
        return self

    def __exit__(self) -> None:
        self.drop()


def read_batch_into(
    reader: ParserFileReader, feature_set: InputFeatureSet, parser_batch: ParserBatch
) -> bool:
    return PARSE_LIB.read_batch_into(reader._ptr, feature_set, parser_batch._ptr)


class BatchLoader:
    def __init__(
        self, files: list[str], feature_set: InputFeatureSet, batch_size: int
    ) -> None:
        assert files
        self._feature_set = feature_set
        self._files = files
        self._file_index = 0
        self._reader = ParserFileReader(self._files[self._file_index])
        self._batch = ParserBatch(batch_size, feature_set.max_indices())

    def read_batch(self, device: torch.device) -> Batch:
        while not read_batch_into(self._reader, self._feature_set, self._batch):
            self._reader.drop()
            self._file_index = (self._file_index + 1) % len(self._files)
            self._reader = ParserFileReader(self._files[self._file_index])
        return self._batch.to_pytorch_batch(device)

    def drop(self) -> None:
        self._reader.drop()
        self._batch.drop()

    def __enter__(self) -> BatchLoader:
        return self

    def __exit__(self) -> None:
        self.drop()

from __future__ import annotations

from dataclasses import dataclass
from enum import IntEnum
from typing import Callable

import ctypes
import os

import numpy as np
import torch


def _load_parse_lib():
    path = "./libparse.dll" if os.name == "nt" else "./libparse.so"
    lib = ctypes.cdll.LoadLibrary(path)

    lib.batch_get_capacity.restype = ctypes.c_uint32
    lib.batch_get_len.restype = ctypes.c_uint32
    lib.batch_get_feature_buffer_ptr.restype = ctypes.POINTER(ctypes.c_int64)
    lib.batch_get_feature_values_ptr.restype = ctypes.POINTER(ctypes.c_float)
    lib.batch_get_feature_count.restype = ctypes.c_uint32
    lib.batch_get_tensors_per_board.restype = ctypes.c_uint32
    lib.batch_get_cp_ptr.restype = ctypes.POINTER(ctypes.c_float)
    lib.batch_get_wdl_ptr.restype = ctypes.POINTER(ctypes.c_float)
    lib.batch_get_bucket_ptr.restype = ctypes.POINTER(ctypes.c_int32)

    lib.batch_reader_new.restype = ctypes.c_void_p
    lib.batch_reader_dataset_size.restype = ctypes.c_uint64
    lib.batch_reader_drop.restype = None

    lib.input_feature_set_get_max_features.restype = ctypes.c_uint32
    lib.input_feature_set_get_indices_per_feature.restype = ctypes.c_uint32

    lib.bucketing_scheme_get_bucket_count.restype = ctypes.c_uint32

    lib.read_batch.restype = ctypes.c_void_p

    return lib


PARSE_LIB = _load_parse_lib()


class InputFeatureSet(IntEnum):
    BOARD_768 = 0
    HALF_KP = 1
    HALF_KA = 2
    HM_STM_BOARD_192 = 3
    PHASED_HM_STM_BOARD_192 = 4

    def max_features(self) -> int:
        return PARSE_LIB.input_feature_set_get_max_features(self)

    def indices_per_feature(self) -> int:
        return PARSE_LIB.input_feature_set_get_indices_per_feature(self)

class BucketingScheme(IntEnum):
    NO_BUCKETING = 0
    MODIFIED_MATERIAL = 1
    PIECE_COUNT = 2

    def bucket_count(self) -> int:
        return PARSE_LIB.bucketing_scheme_get_bucket_count(self)


@dataclass
class Batch:
    indices: list[torch.Tensor]
    values: list[torch.Tensor]
    cp: torch.Tensor
    wdl: torch.Tensor
    buckets: torch.Tensor
    size: int


class ParserBatch:
    def __init__(self, ptr: ctypes.c_void_p) -> None:
        self._ptr = ptr
        if self._ptr.value is None:
            raise Exception("Failed to create batch")

    def drop(self) -> None:
        pass

    def __enter__(self) -> ParserBatch:
        return self

    def __exit__(self) -> None:
        self.drop()

    def get_capacity(self) -> int:
        return PARSE_LIB.batch_get_capacity(self._ptr)

    def get_len(self) -> int:
        return PARSE_LIB.batch_get_len(self._ptr)

    def get_feature_buffer_ptr(self, tensor: int) -> ctypes.pointer[ctypes.c_float]:
        return PARSE_LIB.batch_get_feature_buffer_ptr(self._ptr, tensor)

    def get_feature_values_ptr(self, tensor: int) -> ctypes.pointer[ctypes.c_float]:
        return PARSE_LIB.batch_get_feature_values_ptr(self._ptr, tensor)

    def get_feature_count(self, tensor: int) -> int:
        return PARSE_LIB.batch_get_feature_count(self._ptr, tensor)

    def get_tensors_per_board(self) -> int:
        return PARSE_LIB.batch_get_tensors_per_board(self._ptr)

    def get_indices_per_feature(self) -> int:
        return PARSE_LIB.batch_get_indices_per_feature(self._ptr)

    def get_cp_ptr(self) -> ctypes.pointer[ctypes.c_float]:
        return PARSE_LIB.batch_get_cp_ptr(self._ptr)

    def get_wdl_ptr(self) -> ctypes.pointer[ctypes.c_float]:
        return PARSE_LIB.batch_get_wdl_ptr(self._ptr)

    def get_bucket_ptr(self) -> ctypes.pointer[ctypes.c_int32]:
        return PARSE_LIB.batch_get_bucket_ptr(self._ptr)

    def to_pytorch_batch(self, device: torch.device) -> Batch:
        def to_pytorch(array: np.ndarray) -> torch.Tensor:
            tch_array = torch.from_numpy(array)
            if torch.cuda.is_available():
                tch_array = tch_array.pin_memory()
            return tch_array.to(device, non_blocking=True)

        indices_per_feature = self.get_indices_per_feature()
        indices = []
        values = []
        for i in range(self.get_tensors_per_board()):
            count = self.get_feature_count(i)
            indices.append(to_pytorch(
                np.ctypeslib.as_array(
                    self.get_feature_buffer_ptr(i),
                    shape=(count * indices_per_feature,),
                )
            ))
            values.append(to_pytorch(
                np.ctypeslib.as_array(self.get_feature_values_ptr(i), shape=(count,))
            ))

        batch_len = self.get_len()
        cp = to_pytorch(np.ctypeslib.as_array(self.get_cp_ptr(), shape=(batch_len, 1)))
        wdl = to_pytorch(
            np.ctypeslib.as_array(self.get_wdl_ptr(), shape=(batch_len, 1))
        )
        buckets = to_pytorch(np.ctypeslib.as_array(self.get_bucket_ptr(), shape=(batch_len, 1)))

        return Batch(indices, values, cp, wdl, buckets, batch_len)


class ParserBatchReader:
    def __init__(
        self,
        path: str,
        batch_size: int,
        feature_set: InputFeatureSet,
        bucketing_scheme: BucketingScheme
    ) -> None:
        path_buf = ctypes.create_string_buffer(bytes(path, "utf-8"))
        self._ptr = ctypes.c_void_p(PARSE_LIB.batch_reader_new(
            path_buf, batch_size, feature_set, bucketing_scheme
        ))
        if self._ptr.value is None:
            raise Exception("Failed to create file reader")

    def next_batch(self):
        ptr = ctypes.c_void_p(PARSE_LIB.read_batch(self._ptr))
        if ptr.value is None:
            return None
        else:
            return ParserBatch(ptr)

    def dataset_size(self) -> int:
        return PARSE_LIB.batch_reader_dataset_size(self._ptr)

    def drop(self) -> None:
        if self._ptr.value is not None:
            PARSE_LIB.batch_reader_drop(self._ptr)
            self._ptr.value = None

    def __enter__(self) -> ParserBatchReader:
        return self

    def __exit__(self) -> None:
        self.drop()


class BatchLoader:
    def __init__(
        self,
        next_file: Callable[[], str],
        feature_set: InputFeatureSet, bucketing_scheme: BucketingScheme,
        batch_size: int,
    ) -> None:
        self._feature_set = feature_set
        self._bucketing_scheme = bucketing_scheme
        self._next_file = next_file
        self._batch_size = batch_size
        self._reader = ParserBatchReader(next_file(), batch_size, feature_set, bucketing_scheme)

    def read_batch(self, device: torch.device) -> tuple[bool, Batch]:
        batch = self._reader.next_batch()
        if batch is not None:
            return False, batch.to_pytorch_batch(device)

        self._reader.drop()
        self._reader = ParserBatchReader(
            self._next_file(), self._batch_size, self._feature_set, self._bucketing_scheme
        )
        return True, self.read_batch(device)[1]

    def drop(self) -> None:
        if self._reader is not None:
            self._reader.drop()
            self._reader = None

    def __enter__(self) -> BatchLoader:
        return self

    def __exit__(self) -> None:
        self.drop()

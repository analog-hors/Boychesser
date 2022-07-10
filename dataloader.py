import os
import ctypes
from dataclasses import dataclass

import numpy as np
import torch


INPUT_FEATURES = {"Board768": 0, "HalfKP": 1, "HalfKA": 2}

def _load_parse_lib():
    path = "./libparse.dll" if os.name == "nt" else "./libparse.so"
    lib = ctypes.cdll.LoadLibrary(path)

    lib.new_batch.restype = ctypes.c_void_p
    lib.drop_batch.restype = None
    lib.batch_get_batch_size.restype = ctypes.c_uint32
    lib.batch_get_len.restype = ctypes.c_uint32
    lib.batch_get_stm_feature_buffer_ptr.restype = ctypes.POINTER(ctypes.c_int64)
    lib.batch_get_nstm_feature_buffer_ptr.restype = ctypes.POINTER(ctypes.c_int64)
    lib.batch_get_values_ptr.restype = ctypes.POINTER(ctypes.c_float)
    lib.batch_get_feature_buffer_len.restype = ctypes.c_uint32
    lib.batch_get_cp_ptr.restype = ctypes.POINTER(ctypes.c_float)
    lib.batch_get_wdl_ptr.restype = ctypes.POINTER(ctypes.c_float)

    lib.new_file_reader.restype = ctypes.c_void_p
    lib.drop_file_reader.restype = None

    lib.read_board_768_batch_into.restype = ctypes.c_bool
    lib.read_halfkp_batch_into.restype = ctypes.c_bool
    lib.read_halfka_batch_into.restype = ctypes.c_bool

    return lib
PARSE_LIB = _load_parse_lib()

class ParserBatch:
    _inner: ctypes.c_void_p
    
    def __init__(self, batch_size: int, max_features: int):
        self._inner = ctypes.c_void_p(PARSE_LIB.new_batch(
            ctypes.c_uint32(batch_size),
            ctypes.c_uint32(max_features)
        ))
        if self._inner.value == None:
            raise Exception("Failed to create batch")

    def get_batch_size(self):
        return PARSE_LIB.batch_get_batch_size(self._inner)
    
    def get_len(self):
        return PARSE_LIB.batch_get_len(self._inner)

    def get_stm_feature_buffer_ptr(self):
        return PARSE_LIB.batch_get_stm_feature_buffer_ptr(self._inner)

    def get_nstm_feature_buffer_ptr(self):
        return PARSE_LIB.batch_get_nstm_feature_buffer_ptr(self._inner)
    
    def get_values_ptr(self):
        return PARSE_LIB.batch_get_values_ptr(self._inner)

    def get_feature_buffer_len(self):
        return PARSE_LIB.batch_get_feature_buffer_len(self._inner)

    def get_cp_ptr(self):
        return PARSE_LIB.batch_get_cp_ptr(self._inner)

    def get_wdl_ptr(self):
        return PARSE_LIB.batch_get_wdl_ptr(self._inner)
    
    def drop(self):
        if self._inner.value != None:
            PARSE_LIB.drop_batch(self._inner)
            self._inner.value = None

    def __enter__(self):
        return self

    def __exit__(self):
        self.drop()

class ParserFileReader:
    _inner: ctypes.c_void_p
    
    def __init__(self, path: str):
        self._inner = ctypes.c_void_p(PARSE_LIB.new_file_reader(
            ctypes.create_string_buffer(bytes(path, "ascii"))
        ))
        if self._inner.value == None:
            raise Exception("Failed to create file reader")

    def drop(self):
        if self._inner.value != None:
            PARSE_LIB.drop_file_reader(self._inner)
            self._inner.value = None

    def __enter__(self):
        return self

    def __exit__(self):
        self.drop()

@dataclass
class Batch:
    stm_indices: torch.Tensor
    nstm_indices: torch.Tensor
    values: torch.Tensor
    cp: torch.Tensor
    wdl: torch.Tensor
    size: int

def convert_parser_batch(batch: ParserBatch, device: torch.device) -> Batch:
    def to_pytorch(array: np.ndarray) -> torch.Tensor:
        tch_array = torch.from_numpy(array)
        if torch.cuda.is_available():
            tch_array = tch_array.pin_memory()
        return torch.from_numpy(array).to(device, non_blocking=True)

    feature_buffer_len = batch.get_batch_size()
    boards_stm = to_pytorch(
        np.ctypeslib.as_array(batch.get_stm_feature_buffer_ptr(), shape=(feature_buffer_len, 2)).T
    )
    boards_nstm = to_pytorch(
        np.ctypeslib.as_array(batch.get_nstm_feature_buffer_ptr(), shape=(feature_buffer_len, 2)).T
    )
    values = to_pytorch(
        np.ctypeslib.as_array(batch.get_values_ptr(), shape=(feature_buffer_len,))
    )

    batch_len = batch.get_len()
    cp = to_pytorch(
        np.ctypeslib.as_array(batch.get_cp_ptr(), shape=(batch_len, 1))
    )
    wdl = to_pytorch(
        np.ctypeslib.as_array(batch.get_wdl_ptr(), shape=(batch_len, 1))
    )

    return Batch(boards_stm, boards_nstm, values, cp, wdl, batch_len)

def read_board_768_batch_into(reader: ParserFileReader, parser_batch: ParserBatch) -> bool:
    return PARSE_LIB.read_board_768_batch_into(reader._inner, parser_batch._inner)

def read_halfkp_batch_into(reader: ParserFileReader, parser_batch: ParserBatch) -> bool:
    return PARSE_LIB.read_halfkp_batch_into(reader._inner, parser_batch._inner)

def read_halfka_batch_into(reader: ParserFileReader, parser_batch: ParserBatch) -> bool:
    return PARSE_LIB.read_halfka_batch_into(reader._inner, parser_batch._inner)

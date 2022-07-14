use std::ffi::CStr;
use std::os::raw::c_char;

use batch::Batch;
use input_features::{InputFeatureSet, Board768, HalfKp, HalfKa};
use data_loader::FileReader;

mod batch;
mod data_loader;
mod input_features;

#[no_mangle]
pub unsafe extern "C" fn batch_new(batch_size: u32, max_features: u32) -> *mut Batch {
    let batch = Batch::new(batch_size as usize, max_features as usize);
    Box::into_raw(Box::new(batch))
}

#[no_mangle]
pub unsafe extern "C" fn batch_drop(batch: *mut Batch) {
    Box::from_raw(batch);
}

#[no_mangle]
pub unsafe extern "C" fn batch_clear(batch: *mut Batch) {
    batch.as_mut().unwrap().clear();
}

macro_rules! export_batch_getters {
    ($($getter:ident $(as $cast_type:ty)?: $exported:ident -> $type:ty,)*) => {$(
        #[no_mangle]
        pub unsafe extern "C" fn $exported(batch: *mut Batch) -> $type {
            batch.as_mut().unwrap().$getter() $(as $cast_type)*
        }
    )*}
}
export_batch_getters! {
    capacity as u32         : batch_get_capacity -> u32,
    len as u32              : batch_get_len -> u32,
    stm_feature_buffer_ptr  : batch_get_stm_feature_buffer_ptr -> *const i64,
    nstm_feature_buffer_ptr : batch_get_nstm_feature_buffer_ptr -> *const i64,
    values_ptr              : batch_get_values_ptr -> *const f32,
    total_features as u32   : batch_get_total_features -> u32,
    cp_ptr                  : batch_get_cp_ptr -> *const f32,
    wdl_ptr                 : batch_get_wdl_ptr -> *const f32,
}

#[no_mangle]
pub unsafe extern "C" fn file_reader_new(path: *const c_char) -> *mut FileReader {
    pub unsafe fn try_new_file_reader(path: *const c_char) -> Option<FileReader> {
        let path = CStr::from_ptr(path).to_str().ok()?;
        let reader = FileReader::new(path).ok()?;
        Some(reader)
    }
    if let Some(reader) = try_new_file_reader(path) {
        Box::into_raw(Box::new(reader))
    } else {
        std::ptr::null_mut()
    }
}

#[no_mangle]
pub unsafe extern "C" fn file_reader_drop(reader: *mut FileReader) {
    Box::from_raw(reader);
}

#[repr(C)]
pub enum InputFeatureSetType {
    Board768,
    HalfKp,
    HalfKa
}

#[no_mangle]
pub unsafe extern "C" fn input_feature_set_get_max_indices(feature_set: InputFeatureSetType) -> u32 {
    let max_features = match feature_set {
        InputFeatureSetType::Board768 => Board768::MAX_INDICES,
        InputFeatureSetType::HalfKp => HalfKp::MAX_INDICES,
        InputFeatureSetType::HalfKa => HalfKa::MAX_INDICES
    };
    max_features as u32
}

#[no_mangle]
pub unsafe extern "C" fn read_batch_into(reader: *mut FileReader, feature_set: InputFeatureSetType, batch: *mut Batch) -> bool {
    let reader = reader.as_mut().unwrap();
    let batch = batch.as_mut().unwrap();
    match feature_set {
        InputFeatureSetType::Board768 => data_loader::read_batch_into::<Board768>(reader, batch),
        InputFeatureSetType::HalfKp => data_loader::read_batch_into::<HalfKp>(reader, batch),
        InputFeatureSetType::HalfKa => data_loader::read_batch_into::<HalfKa>(reader, batch),
    }
}

use std::ffi::CStr;
use std::os::raw::c_char;

use batch::Batch;
use input_features::{InputFeature, Board768, HalfKp, HalfKa};
use data_loader::FileReader;

mod batch;
mod data_loader;
mod input_features;

#[no_mangle]
pub unsafe extern "C" fn new_batch(batch_size: u32, max_features: u32) -> *mut Batch {
    let batch = Batch::new(batch_size as usize, max_features as usize);
    Box::into_raw(Box::new(batch))
}

#[no_mangle]
pub unsafe extern "C" fn drop_batch(batch: *mut Batch) {
    Box::from_raw(batch);
}

#[no_mangle]
pub unsafe extern "C" fn clear_batch(batch: *mut Batch) {
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
    batch_size as u32         : batch_get_batch_size -> u32,
    len as u32                : batch_get_len -> u32,
    stm_feature_buffer_ptr    : batch_get_stm_feature_buffer_ptr -> *const i64,
    nstm_feature_buffer_ptr   : batch_get_nstm_feature_buffer_ptr -> *const i64,
    values_ptr                : batch_get_values_ptr -> *const f32,
    feature_buffer_len as u32 : batch_get_feature_buffer_len -> u32,
    cp_ptr                    : batch_get_cp_ptr -> *const f32,
    wdl_ptr                   : batch_get_wdl_ptr -> *const f32,
}

#[no_mangle]
pub unsafe extern "C" fn new_file_reader(path: *const c_char) -> *mut FileReader {
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
pub unsafe extern "C" fn drop_file_reader(reader: *mut FileReader) {
    Box::from_raw(reader);
}

unsafe fn c_read_batch_into<F: InputFeature>(reader: *mut FileReader, batch: *mut Batch) -> bool {
    let reader = reader.as_mut().unwrap();
    let batch = batch.as_mut().unwrap();
    data_loader::read_batch_into::<F>(reader, batch)
}

#[no_mangle]
pub unsafe extern "C" fn read_board_768_batch_into(reader: *mut FileReader, batch: *mut Batch) -> bool {
    c_read_batch_into::<Board768>(reader, batch)
}

#[no_mangle]
pub unsafe extern "C" fn read_halfkp_batch_into(reader: *mut FileReader, batch: *mut Batch) -> bool {
    c_read_batch_into::<HalfKp>(reader, batch)
}

#[no_mangle]
pub unsafe extern "C" fn read_halfka_batch_into(reader: *mut FileReader, batch: *mut Batch) -> bool {
    c_read_batch_into::<HalfKa>(reader, batch)
}

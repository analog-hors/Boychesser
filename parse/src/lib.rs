use std::ffi::CStr;
use std::os::raw::c_char;

use batch_loader::BatchLoader;
use bl768::BatchLoader768;
use bl_half_kp::BatchLoaderHalfKp;

mod batch_loader;
mod bl768;
mod bl_half_kp;

type Loader = BatchLoaderHalfKp;

#[no_mangle]
pub extern "C" fn new_batch_loader(batch_size: i32) -> *mut Loader {
    let batch_loader = Box::new(Loader::new(batch_size as usize));
    let batch_loader = Box::leak(batch_loader) as *mut Loader;
    batch_loader
}

#[no_mangle]
pub extern "C" fn open_file(batch_loader: *mut Loader, file: *const c_char) {
    let file = unsafe { CStr::from_ptr(file) }.to_str().unwrap();
    unsafe {
        batch_loader.as_mut().unwrap().set_file(file);
    }
}

#[no_mangle]
pub extern "C" fn close_file(batch_loader: *mut Loader) {
    unsafe {
        batch_loader.as_mut().unwrap().close_file();
    }
}

#[no_mangle]
pub extern "C" fn read_batch(batch_loader: *mut Loader) -> bool {
    unsafe { batch_loader.as_mut().unwrap().read() }
}

#[no_mangle]
pub extern "C" fn boards_stm(batch_loader: *mut Loader) -> *const i64 {
    unsafe { batch_loader.as_mut().unwrap().stm_indices() }
}

#[no_mangle]
pub extern "C" fn boards_nstm(batch_loader: *mut Loader) -> *const i64 {
    unsafe { batch_loader.as_mut().unwrap().nstm_indices() }
}

#[no_mangle]
pub extern "C" fn values(batch_loader: *mut Loader) -> *const f32 {
    unsafe { batch_loader.as_mut().unwrap().values() }
}

#[no_mangle]
pub extern "C" fn count(batch_loader: *mut Loader) -> u32 {
    unsafe { batch_loader.as_mut().unwrap().count() }
}

#[no_mangle]
pub extern "C" fn cp(batch_loader: *mut Loader) -> *const f32 {
    unsafe { batch_loader.as_mut().unwrap().cp() }
}

#[no_mangle]
pub extern "C" fn wdl(batch_loader: *mut Loader) -> *const f32 {
    unsafe { batch_loader.as_mut().unwrap().wdl() }
}

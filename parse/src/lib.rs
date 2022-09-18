use std::ffi::CStr;
use std::os::raw::c_char;

use batch::Batch;
use bucketing::{BucketingScheme, ModifiedMaterial, NoBucketing, PieceCount};
use data_loader::FileReader;
use input_features::{
    Board768, Board768Cuda, HalfKa, HalfKaCuda, HalfKp, HalfKpCuda, InputFeatureSet,
};

mod batch;
mod bucketing;
mod data_loader;
mod input_features;

#[no_mangle]
pub unsafe extern "C" fn batch_new(
    batch_size: u32,
    max_features: u32,
    indices_per_feature: u32,
) -> *mut Batch {
    let batch = Batch::new(
        batch_size as usize,
        max_features as usize,
        indices_per_feature as usize,
    );
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
    capacity as u32                 : batch_get_capacity -> u32,
    len as u32                      : batch_get_len -> u32,
    stm_feature_buffer_ptr          : batch_get_stm_feature_buffer_ptr -> *const i64,
    nstm_feature_buffer_ptr         : batch_get_nstm_feature_buffer_ptr -> *const i64,
    values_ptr                      : batch_get_values_ptr -> *const f32,
    total_features as u32           : batch_get_total_features -> u32,
    indices_per_feature as u32      : batch_get_indices_per_feature -> u32,
    cp_ptr                          : batch_get_cp_ptr -> *const f32,
    wdl_ptr                         : batch_get_wdl_ptr -> *const f32,
    bucket_ptr                      : batch_get_bucket_ptr -> *const i32,
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
    HalfKa,
    Board768Cuda,
    HalfKpCuda,
    HalfKaCuda,
}

#[no_mangle]
pub unsafe extern "C" fn input_feature_set_get_max_features(
    feature_set: InputFeatureSetType,
) -> u32 {
    let max_features = match feature_set {
        InputFeatureSetType::Board768 => Board768::MAX_FEATURES,
        InputFeatureSetType::HalfKp => HalfKp::MAX_FEATURES,
        InputFeatureSetType::HalfKa => HalfKa::MAX_FEATURES,
        InputFeatureSetType::Board768Cuda => Board768Cuda::MAX_FEATURES,
        InputFeatureSetType::HalfKpCuda => HalfKpCuda::MAX_FEATURES,
        InputFeatureSetType::HalfKaCuda => HalfKaCuda::MAX_FEATURES,
    };
    max_features as u32
}

#[no_mangle]
pub unsafe extern "C" fn input_feature_set_get_indices_per_feature(
    feature_set: InputFeatureSetType,
) -> u32 {
    let indices_per_feature = match feature_set {
        InputFeatureSetType::Board768 => Board768::INDICES_PER_FEATURE,
        InputFeatureSetType::HalfKp => HalfKp::INDICES_PER_FEATURE,
        InputFeatureSetType::HalfKa => HalfKa::INDICES_PER_FEATURE,
        InputFeatureSetType::Board768Cuda => Board768Cuda::INDICES_PER_FEATURE,
        InputFeatureSetType::HalfKpCuda => HalfKpCuda::INDICES_PER_FEATURE,
        InputFeatureSetType::HalfKaCuda => HalfKaCuda::INDICES_PER_FEATURE,
    };
    indices_per_feature as u32
}

#[repr(C)]
pub enum BucketingSchemeType {
    NoBucketing,
    ModifiedMaterial,
    PieceCount,
}

#[no_mangle]
pub unsafe extern "C" fn bucketing_scheme_get_bucket_count(
    bucketing_scheme: BucketingSchemeType,
) -> u32 {
    let bucket_count = match bucketing_scheme {
        BucketingSchemeType::NoBucketing => NoBucketing::BUCKET_COUNT,
        BucketingSchemeType::ModifiedMaterial => ModifiedMaterial::BUCKET_COUNT,
        BucketingSchemeType::PieceCount => PieceCount::BUCKET_COUNT,
    };
    bucket_count as u32
}

#[no_mangle]
pub unsafe extern "C" fn read_batch_into(
    reader: *mut FileReader,
    feature_set: InputFeatureSetType,
    bucketing_scheme: BucketingSchemeType,
    batch: *mut Batch,
) -> bool {
    let reader = reader.as_mut().unwrap();
    let batch = batch.as_mut().unwrap();
    match feature_set {
        InputFeatureSetType::Board768 => match bucketing_scheme {
            BucketingSchemeType::NoBucketing => {
                data_loader::read_batch_into::<Board768, NoBucketing>(reader, batch)
            }
            BucketingSchemeType::ModifiedMaterial => {
                data_loader::read_batch_into::<Board768, ModifiedMaterial>(reader, batch)
            }
            BucketingSchemeType::PieceCount => {
                data_loader::read_batch_into::<Board768, PieceCount>(reader, batch)
            }
        },
        InputFeatureSetType::HalfKp => match bucketing_scheme {
            BucketingSchemeType::NoBucketing => {
                data_loader::read_batch_into::<HalfKp, NoBucketing>(reader, batch)
            }
            BucketingSchemeType::ModifiedMaterial => {
                data_loader::read_batch_into::<HalfKp, ModifiedMaterial>(reader, batch)
            }
            BucketingSchemeType::PieceCount => {
                data_loader::read_batch_into::<HalfKp, PieceCount>(reader, batch)
            }
        },
        InputFeatureSetType::HalfKa => match bucketing_scheme {
            BucketingSchemeType::NoBucketing => {
                data_loader::read_batch_into::<HalfKa, NoBucketing>(reader, batch)
            }
            BucketingSchemeType::ModifiedMaterial => {
                data_loader::read_batch_into::<HalfKa, ModifiedMaterial>(reader, batch)
            }
            BucketingSchemeType::PieceCount => {
                data_loader::read_batch_into::<HalfKa, PieceCount>(reader, batch)
            }
        },
        InputFeatureSetType::Board768Cuda => match bucketing_scheme {
            BucketingSchemeType::NoBucketing => {
                data_loader::read_batch_into::<Board768Cuda, NoBucketing>(reader, batch)
            }
            BucketingSchemeType::ModifiedMaterial => {
                data_loader::read_batch_into::<Board768Cuda, ModifiedMaterial>(reader, batch)
            }
            BucketingSchemeType::PieceCount => {
                data_loader::read_batch_into::<Board768Cuda, PieceCount>(reader, batch)
            }
        },
        InputFeatureSetType::HalfKpCuda => match bucketing_scheme {
            BucketingSchemeType::NoBucketing => {
                data_loader::read_batch_into::<HalfKpCuda, NoBucketing>(reader, batch)
            }
            BucketingSchemeType::ModifiedMaterial => {
                data_loader::read_batch_into::<HalfKpCuda, ModifiedMaterial>(reader, batch)
            }
            BucketingSchemeType::PieceCount => {
                data_loader::read_batch_into::<HalfKpCuda, PieceCount>(reader, batch)
            }
        },
        InputFeatureSetType::HalfKaCuda => match bucketing_scheme {
            BucketingSchemeType::NoBucketing => {
                data_loader::read_batch_into::<HalfKaCuda, NoBucketing>(reader, batch)
            }
            BucketingSchemeType::ModifiedMaterial => {
                data_loader::read_batch_into::<HalfKaCuda, ModifiedMaterial>(reader, batch)
            }
            BucketingSchemeType::PieceCount => {
                data_loader::read_batch_into::<HalfKaCuda, PieceCount>(reader, batch)
            }
        },
    }
}

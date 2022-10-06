use cozy_chess::Board;

use crate::batch::EntryFeatureWriter;

mod board_768;
mod half_ka;
mod half_kp;

pub use board_768::Board768;
pub use board_768::Board768Cuda;
pub use half_ka::HalfKa;
pub use half_ka::HalfKaCuda;
pub use half_kp::HalfKp;
pub use half_kp::HalfKpCuda;

pub trait InputFeatureSet {
    const INDICES_PER_FEATURE: usize;
    const MAX_FEATURES: usize;

    fn add_features(board: Board, entry: EntryFeatureWriter);
}

#[repr(C)]
#[derive(Copy, Clone, Debug)]
pub enum InputFeatureSetType {
    Board768,
    HalfKp,
    HalfKa,
    Board768Cuda,
    HalfKpCuda,
    HalfKaCuda,
}

impl InputFeatureSetType {
    pub fn max_features(self) -> usize {
        match self {
            InputFeatureSetType::Board768 => Board768::MAX_FEATURES,
            InputFeatureSetType::HalfKp => HalfKp::MAX_FEATURES,
            InputFeatureSetType::HalfKa => HalfKa::MAX_FEATURES,
            InputFeatureSetType::Board768Cuda => Board768Cuda::MAX_FEATURES,
            InputFeatureSetType::HalfKpCuda => HalfKpCuda::MAX_FEATURES,
            InputFeatureSetType::HalfKaCuda => HalfKaCuda::MAX_FEATURES,
        }
    }

    pub fn indices_per_feature(self) -> usize {
        match self {
            InputFeatureSetType::Board768 => Board768::INDICES_PER_FEATURE,
            InputFeatureSetType::HalfKp => HalfKp::INDICES_PER_FEATURE,
            InputFeatureSetType::HalfKa => HalfKa::INDICES_PER_FEATURE,
            InputFeatureSetType::Board768Cuda => Board768Cuda::INDICES_PER_FEATURE,
            InputFeatureSetType::HalfKpCuda => HalfKpCuda::INDICES_PER_FEATURE,
            InputFeatureSetType::HalfKaCuda => HalfKaCuda::INDICES_PER_FEATURE,
        }
    }
}

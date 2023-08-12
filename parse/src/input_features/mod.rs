use cozy_chess::Board;

use crate::batch::EntryFeatureWriter;

mod board_768;
mod half_ka;
mod half_kp;
mod hm_stm_board_192;
mod phased_hm_stm_board_192;
mod phased_stm_board_384;
mod ice4_input_features;

pub use board_768::Board768;
pub use half_ka::HalfKa;
pub use half_kp::HalfKp;
pub use hm_stm_board_192::HmStmBoard192;
pub use phased_hm_stm_board_192::PhasedHmStmBoard192;
pub use phased_stm_board_384::PhasedStmBoard384;
pub use ice4_input_features::Ice4InputFeatures;

pub use ice4_input_features::ICE4_FEATURE_COUNT;

pub trait InputFeatureSet {
    const INDICES_PER_FEATURE: usize;
    const MAX_FEATURES: usize;
    const TENSORS_PER_BOARD: usize;

    fn add_features(board: Board, entry: EntryFeatureWriter);
}

#[repr(C)]
#[derive(Copy, Clone, Debug)]
pub enum InputFeatureSetType {
    Board768,
    HalfKp,
    HalfKa,
    HmStmBoard192,
    PhasedHmStmBoard192,
    PhasedStmBoard384,
    Ice4InputFeatures,
}

impl InputFeatureSetType {
    pub fn max_features(self) -> usize {
        match self {
            InputFeatureSetType::Board768 => Board768::MAX_FEATURES,
            InputFeatureSetType::HalfKp => HalfKp::MAX_FEATURES,
            InputFeatureSetType::HalfKa => HalfKa::MAX_FEATURES,
            InputFeatureSetType::HmStmBoard192 => HmStmBoard192::MAX_FEATURES,
            InputFeatureSetType::PhasedHmStmBoard192 => PhasedHmStmBoard192::MAX_FEATURES,
            InputFeatureSetType::PhasedStmBoard384 => PhasedStmBoard384::MAX_FEATURES,
            InputFeatureSetType::Ice4InputFeatures => Ice4InputFeatures::MAX_FEATURES,
        }
    }

    pub fn indices_per_feature(self) -> usize {
        match self {
            InputFeatureSetType::Board768 => Board768::INDICES_PER_FEATURE,
            InputFeatureSetType::HalfKp => HalfKp::INDICES_PER_FEATURE,
            InputFeatureSetType::HalfKa => HalfKa::INDICES_PER_FEATURE,
            InputFeatureSetType::HmStmBoard192 => HmStmBoard192::INDICES_PER_FEATURE,
            InputFeatureSetType::PhasedHmStmBoard192 => PhasedHmStmBoard192::INDICES_PER_FEATURE,
            InputFeatureSetType::PhasedStmBoard384 => PhasedStmBoard384::INDICES_PER_FEATURE,
            InputFeatureSetType::Ice4InputFeatures => Ice4InputFeatures::INDICES_PER_FEATURE,
        }
    }

    pub fn tensors_per_board(self) -> usize {
        match self {
            InputFeatureSetType::Board768 => Board768::TENSORS_PER_BOARD,
            InputFeatureSetType::HalfKp => HalfKp::TENSORS_PER_BOARD,
            InputFeatureSetType::HalfKa => HalfKa::TENSORS_PER_BOARD,
            InputFeatureSetType::HmStmBoard192 => HmStmBoard192::TENSORS_PER_BOARD,
            InputFeatureSetType::PhasedHmStmBoard192 => PhasedHmStmBoard192::TENSORS_PER_BOARD,
            InputFeatureSetType::PhasedStmBoard384 => PhasedStmBoard384::TENSORS_PER_BOARD,
            InputFeatureSetType::Ice4InputFeatures => Ice4InputFeatures::TENSORS_PER_BOARD,
        }
    }
}
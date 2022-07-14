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

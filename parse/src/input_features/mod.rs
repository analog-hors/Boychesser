use cozy_chess::Board;

use crate::batch::EntryFeatureWriter;

mod board_768;
mod half_kp;
mod half_ka;

pub use board_768::Board768;
pub use half_kp::HalfKp;
pub use half_ka::HalfKa;

pub trait InputFeatureSet {
    const MAX_INDICES: usize;

    fn add_features(board: Board, entry: EntryFeatureWriter);
}

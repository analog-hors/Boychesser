use cozy_chess::{Board, Color, Piece, Square};

use crate::batch::EntryFeatureWriter;

use super::InputFeatureSet;

pub struct Board768;

impl InputFeatureSet for Board768 {
    const MAX_FEATURES: usize = 32;
    const INDICES_PER_FEATURE: usize = 2;
    const TENSORS_PER_BOARD: usize = 2;

    fn add_features(board: Board, mut entry: EntryFeatureWriter) {
        let stm = board.side_to_move();

        for &color in &Color::ALL {
            for &piece in &Piece::ALL {
                for square in board.pieces(piece) & board.colors(color) {
                    let stm_feature = feature(stm, color, piece, square);
                    let nstm_feature = feature(!stm, color, piece, square);
                    entry.add_feature(0, stm_feature as i64, 1.0);
                    entry.add_feature(1, nstm_feature as i64, 1.0);
                }
            }
        }
    }
}

fn feature(perspective: Color, color: Color, piece: Piece, square: Square) -> usize {
    let (square, color) = match perspective {
        Color::White => (square, color),
        Color::Black => (square.flip_rank(), !color),
    };
    let mut index = 0;
    index = index * Color::NUM + color as usize;
    index = index * Piece::NUM + piece as usize;
    index = index * Square::NUM + square as usize;
    index
}

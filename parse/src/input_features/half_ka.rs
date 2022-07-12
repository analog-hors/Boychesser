use cozy_chess::{Board, Color, Piece, Square};

use crate::batch::EntryFeatureWriter;

use super::InputFeatureSet;

pub struct HalfKa;

impl InputFeatureSet for HalfKa {
    const MAX_FEATURES: usize = 32;

    fn add_features(board: Board, mut entry: EntryFeatureWriter) {
        let stm = board.side_to_move();

        let stm_king = board.king(stm);
        let nstm_king = board.king(!stm);

        for &color in &Color::ALL {
            for &piece in &Piece::ALL {
                for square in board.pieces(piece) & board.colors(color) {
                    let stm_feature = feature(stm, stm_king, color, piece, square);
                    let nstm_feature = feature(!stm, nstm_king, color, piece, square);
                    entry.add_feature(stm_feature as i64, nstm_feature as i64);
                }
            }
        }
    }
}

fn feature(perspective: Color, king: Square, color: Color, piece: Piece, square: Square) -> usize {
    let (king, square, color) = match perspective {
        Color::White => (king, square, color),
        Color::Black => (king.flip_rank(), square.flip_rank(), !color)
    };
    let mut index = 0;
    index = index * Square::NUM + king as usize;
    index = index * Color::NUM + color as usize;
    index = index * Piece::NUM + piece as usize;
    index = index * Square::NUM + square as usize;
    index
}

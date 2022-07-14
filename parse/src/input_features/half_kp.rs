use cozy_chess::{Board, Color, Piece, Square};

use crate::batch::EntryFeatureWriter;

use super::InputFeatureSet;

pub struct HalfKp;

pub struct HalfKpCuda;

impl InputFeatureSet for HalfKp {
    const MAX_FEATURES: usize = 30;
    const INDICES_PER_FEATURE: usize = 2;

    fn add_features(board: Board, entry: EntryFeatureWriter) {
        let mut sparse_entry = entry.sparse();
        let stm = board.side_to_move();

        let stm_king = board.king(stm);
        let nstm_king = board.king(!stm);

        for &color in &Color::ALL {
            for &piece in &Piece::ALL {
                if piece == Piece::King {
                    continue;
                }
                for square in board.pieces(piece) & board.colors(color) {
                    let stm_feature = feature(stm, stm_king, color, piece, square);
                    let nstm_feature = feature(!stm, nstm_king, color, piece, square);
                    sparse_entry.add_feature(stm_feature as i64, nstm_feature as i64);
                }
            }
        }
    }
}

impl InputFeatureSet for HalfKpCuda {
    const MAX_FEATURES: usize = 30;
    const INDICES_PER_FEATURE: usize = 1;

    fn add_features(board: Board, entry: EntryFeatureWriter) {
        let mut cuda_entry = entry.cuda();
        let stm = board.side_to_move();

        let stm_king = board.king(stm);
        let nstm_king = board.king(!stm);

        for &color in &Color::ALL {
            for &piece in &Piece::ALL {
                if piece == Piece::King {
                    continue;
                }
                for square in board.pieces(piece) & board.colors(color) {
                    let stm_feature = feature(stm, stm_king, color, piece, square);
                    let nstm_feature = feature(!stm, nstm_king, color, piece, square);
                    cuda_entry.add_feature(stm_feature as i64, nstm_feature as i64);
                }
            }
        }
    }
}

fn feature(perspective: Color, king: Square, color: Color, piece: Piece, square: Square) -> usize {
    let (king, square, color) = match perspective {
        Color::White => (king, square, color),
        Color::Black => (king.flip_rank(), square.flip_rank(), !color),
    };
    let mut index = 0;
    index = index * Square::NUM + king as usize;
    index = index * Color::NUM + color as usize;
    index = index * (Piece::NUM - 1) + piece as usize; // sub 1 since no king
    index = index * Square::NUM + square as usize;
    index
}

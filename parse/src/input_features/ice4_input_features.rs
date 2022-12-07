use cozy_chess::{get_bishop_moves, get_rook_moves, Board, Color, File, Piece, Rank, Square};

use crate::batch::EntryFeatureWriter;

use super::InputFeatureSet;

pub struct Ice4InputFeatures;

impl InputFeatureSet for Ice4InputFeatures {
    const MAX_FEATURES: usize = 32;
    const INDICES_PER_FEATURE: usize = 2;
    const TENSORS_PER_BOARD: usize = 2;

    fn add_features(board: Board, mut entry: EntryFeatureWriter) {
        let stm = board.side_to_move();

        let phase = (board.pieces(Piece::Knight).len()
            + board.pieces(Piece::Bishop).len()
            + 2 * board.pieces(Piece::Rook).len()
            + 4 * board.pieces(Piece::Queen).len()) as f32
            / 24.0;

        for &color in &Color::ALL {
            for &piece in &Piece::ALL {
                for square in board.pieces(piece) & board.colors(color) {
                    let tensor = match color == stm {
                        false => 0,
                        true => 1,
                    };
                    let feature = feature(color, piece, square);
                    entry.add_feature(tensor, feature as i64, phase);
                    entry.add_feature(tensor, feature as i64 + 384, 1.0 - phase);
                }
            }
        }

        for &color in &Color::ALL {
            for square in board.pieces(Piece::Pawn) & board.colors(color) {
                let mut passer_mask = square.file().adjacent() | square.file().bitboard();
                match color {
                    Color::White => {
                        for r in 0..=square.rank() as usize {
                            passer_mask &= !Rank::index(r).bitboard();
                        }
                    }
                    Color::Black => {
                        for r in square.rank() as usize + 1..8 {
                            passer_mask &= !Rank::index(r).bitboard();
                        }
                    }
                }

                if !passer_mask.is_disjoint(board.colored_pieces(!color, Piece::Pawn)) {
                    continue;
                }
                
                let tensor = match color == stm {
                    false => 0,
                    true => 1,
                };
                let feature = match color {
                    Color::White => square.flip_rank(),
                    Color::Black => square,
                } as usize + 768;
                entry.add_feature(tensor, feature as i64, phase);
                entry.add_feature(tensor, feature as i64 + 64, 1.0 - phase);
            }
        }
    }
}

fn feature(color: Color, piece: Piece, square: Square) -> usize {
    let square = match color {
        Color::White => square,
        Color::Black => square.flip_rank(),
    };
    let mut index = 0;
    index = index * Piece::NUM + piece as usize;
    index = index * Square::NUM + square as usize;
    index
}

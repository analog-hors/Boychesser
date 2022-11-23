use cozy_chess::{Board, Color, File, Piece, Square};

use crate::batch::EntryFeatureWriter;

use super::InputFeatureSet;

pub struct HmStmBoard192;

impl InputFeatureSet for HmStmBoard192 {
    const MAX_FEATURES: usize = 16;
    const INDICES_PER_FEATURE: usize = 2;
    const TENSORS_PER_BOARD: usize = 4;

    fn add_features(board: Board, mut entry: EntryFeatureWriter) {
        let stm = board.side_to_move();

        for &color in &Color::ALL {
            for &piece in &Piece::ALL {
                for square in board.pieces(piece) & board.colors(color) {
                    let tensor = match (square.file() >= File::E, color == stm) {
                        (false, true) => 0,
                        (true, true) => 1,
                        (false, false) => 2,
                        (true, false) => 3,
                    };
                    let feature = feature(color, piece, square);
                    entry.add_feature(tensor, feature as i64, 1.0);
                }
            }
        }
    }
}

fn feature(color: Color, piece: Piece, square: Square) -> usize {
    let rank = match color {
        Color::White => square.rank(),
        Color::Black => square.rank().flip(),
    };
    let file = match square.file() >= File::E {
        true => square.file().flip(),
        false => square.file(),
    };
    let mut index = 0;
    index = index * Piece::NUM + piece as usize;
    index = index * Square::NUM / 2 + rank as usize * 4 + file as usize;
    index
}

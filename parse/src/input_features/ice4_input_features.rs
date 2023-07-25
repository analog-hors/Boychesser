use cozy_chess::*;

use crate::batch::EntryFeatureWriter;

use super::InputFeatureSet;

pub struct Ice4InputFeatures;

pub const ICE4_FEATURE_COUNT: usize = TOTAL_FEATURES * 2;

macro_rules! offsets {
    ($name:ident: $($rest:tt)*) => {
        const $name: usize = 0;
        offsets!([] $($rest)*);
    };
    ([$($val:literal)*] $size:literal; $next:ident : $($rest:tt)*) => {
        const $next: usize = $($val +)* $size;
        offsets!([$($val)* $size] $($rest)*);
    };
    ([$($val:literal)*] $size:literal;) => {
        const TOTAL_FEATURES: usize = $($val +)* $size;
    };
}

offsets! {
    MATERIAL: 6;
    RANK: 6;
    // RANK_SQ: 6;
    OUTSIDE_FILE: 6;
    OUTSIDE_RANK: 6;
    MOBILITY: 6;
    KING_FILE: 6;
}

impl InputFeatureSet for Ice4InputFeatures {
    const MAX_FEATURES: usize = 48;
    const INDICES_PER_FEATURE: usize = 2;
    const TENSORS_PER_BOARD: usize = 1;

    fn add_features(board: Board, mut entry: EntryFeatureWriter) {
        let phase = (board.pieces(Piece::Knight).len()
            + board.pieces(Piece::Bishop).len()
            + 2 * board.pieces(Piece::Rook).len()
            + 4 * board.pieces(Piece::Queen).len()) as f32
            / 24.0;

        let mut features = [0i8; TOTAL_FEATURES];

        let w_king = board.king(Color::White);
        let b_king = board.king(Color::Black);

        for &piece in &Piece::ALL {
            for square in board.pieces(piece) {
                let color = board.color_on(square).unwrap();

                let (inc, king, _opp_king) = match color {
                    Color::White => (1, w_king, b_king),
                    Color::Black => (-1, b_king, w_king),
                };

                let cnt = match piece {
                    Piece::Queen | Piece::King => (get_rook_moves(square, board.occupied())
                        | get_bishop_moves(square, board.occupied()))
                    .len(),
                    Piece::Rook => get_rook_moves(square, board.occupied()).len(),
                    Piece::Bishop => get_bishop_moves(square, board.occupied()).len(),
                    _ => 0,
                } as i8;

                features[MOBILITY + piece as usize] += inc * cnt;

                let square = match color {
                    Color::White => square,
                    Color::Black => square.flip_rank(),
                };

                features[MATERIAL + piece as usize] += inc;

                features[RANK + piece as usize] += square.rank() as i8 * inc;
                // features[RANK_SQ + piece as usize] += square.rank() as i8 * square.rank() as i8 * inc;

                features[OUTSIDE_FILE + piece as usize] +=
                    (square.file() as i8).min(7 - square.file() as i8) * inc;
                features[OUTSIDE_RANK + piece as usize] +=
                    (square.rank() as i8).min(7 - square.rank() as i8) * inc;
                features[KING_FILE + piece as usize] +=
                    (square.file() as i8).abs_diff(king.file() as i8) as i8 * inc;
                //features[OPP_KING_FILE + piece as usize] +=
                //    (square.file() as i8).abs_diff(opp_king.file() as i8) as i8 * inc;
            }
        }

        for (i, &v) in features.iter().enumerate().filter(|&(_, &v)| v != 0) {
            entry.add_feature(0, i as i64, v as f32 * phase);
            entry.add_feature(0, (i + TOTAL_FEATURES) as i64, v as f32 * (1.0 - phase));
        }
    }
}

// fn hm_feature(square: Square) -> usize {
//     let square = match square.file() > File::D {
//         true => square.flip_file(),
//         false => square,
//     };
//     let square = match square.rank() > Rank::Fourth {
//         true => square.flip_rank(),
//         false => square,
//     };
//     square.rank() as usize * 4 + square.file() as usize
// }

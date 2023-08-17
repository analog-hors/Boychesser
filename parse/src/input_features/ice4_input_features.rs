#![allow(unused)]

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
    OPPOSITE_PAWN_PST: 32;
    PAWN_PST: 32;
    KNIGHT_PST: 32;
    BISHOP_PST: 32;
    ROOK_PST: 32;
    QUEEN_PST: 32;
    KING_PST: 32;
    MATERIAL: 7;
    MOBILITY: 4;
    TEMPO: 1;
    OWN_PAWNS_AHEAD: 7;
}

impl InputFeatureSet for Ice4InputFeatures {
    const MAX_FEATURES: usize = 64;
    const INDICES_PER_FEATURE: usize = 2;
    const TENSORS_PER_BOARD: usize = 1;

    fn add_features(board: Board, mut entry: EntryFeatureWriter) {
        let phase = (board.pieces(Piece::Knight).len()
            + board.pieces(Piece::Bishop).len()
            + 2 * board.pieces(Piece::Rook).len()
            + 4 * board.pieces(Piece::Queen).len()) as f32
            / 24.0;

        let mut features = [0i8; TOTAL_FEATURES];

        features[TEMPO] += match board.side_to_move() {
            Color::White => 1,
            Color::Black => -1,
        };

        let w_king = board.king(Color::White);
        let b_king = board.king(Color::Black);

        for &piece in &Piece::ALL {
            for square in board.pieces(piece) {
                let color = board.color_on(square).unwrap();

                let (inc, king, opp_king, sq) = match color {
                    Color::White => (1, w_king, b_king, square),
                    Color::Black => (-1, b_king, w_king, square.flip_rank()),
                };

                let mut piece_index = piece as usize + 1;
                let on_king_half =
                    (square.file() > File::D) == (board.king(color).file() > File::D);
                if piece == Piece::Pawn && !on_king_half {
                    piece_index -= 1;
                }
                features[OPPOSITE_PAWN_PST + piece_index * 32 + hm_feature(sq)] += inc;

                let cnt = match piece {
                    Piece::Queen | Piece::King => {
                        get_rook_moves(square, board.occupied())
                            | get_bishop_moves(square, board.occupied())
                    }
                    Piece::Rook => get_rook_moves(square, board.occupied()),
                    Piece::Bishop => get_bishop_moves(square, board.occupied()),
                    _ => BitBoard::EMPTY,
                }
                .len() as i8;

                features[MOBILITY + piece_index - 3] += inc * cnt;

                features[MATERIAL + piece_index] += inc;

                // features[OWN_PAWNS_FILE + piece_index] += (board.colored_pieces(color, Piece::Pawn)
                //     & square.file().bitboard())
                // .len() as i8
                //     * inc;

                // let distance = std::cmp::max(
                //     (square.file() as i8 - opp_king.file() as i8).abs(),
                //     (square.rank() as i8 - opp_king.rank() as i8).abs(),
                // );
                // features[OPP_KING_TROPISM + piece_index] += inc * distance;

                let ahead = match color {
                    Color::White => 0x0101010101010100 << square as usize,
                    Color::Black => 0x0080808080808080 >> 63 - square as usize,
                };

                features[OWN_PAWNS_AHEAD + piece_index] +=
                    (board.colored_pieces(color, Piece::Pawn) & BitBoard(ahead)).len() as i8 * inc;

                // features[OPP_PAWNS_AHEAD + piece_index] +=
                //     (board.colored_pieces(!color, Piece::Pawn) & BitBoard(ahead)).len() as i8 * inc;

                // if piece == Piece::Pawn {
                //     features[PASSED_PAWN] += (board.colored_pieces(!color, Piece::Pawn)
                //         & BitBoard(ahead | ahead << 1 & 0x7F7F7F7F7F7F7F7F | ahead >> 1 & 0xFEFEFEFEFEFEFEFE))
                //     .is_empty() as i8
                //         * inc;
                // }

                // features[PIECE_COUNT + piece as usize] +=
                //     board.colored_pieces(color, piece).len()
                //         as i8
                //         * inc;

                // features[BISHOP_PAIR] += if piece == Piece::King {
                //     board.colored_pieces(color, Piece::Bishop).len() / 2
                // } else {
                //     0
                // } as i8 * inc;

                // let square = match color {
                //     Color::White => square,
                //     Color::Black => square.flip_rank(),
                // };

                // features[MATERIAL + piece as usize] += inc;

                // features[RANK + piece as usize] += square.rank() as i8 * inc;

                // features[OUTSIDE_FILE + piece as usize] +=
                //     (square.file() as i8).min(7 - square.file() as i8) * inc;
                // features[OUTSIDE_RANK + piece as usize] +=
                //     (square.rank() as i8).min(7 - square.rank() as i8) * inc;
                // features[KING_FILE + piece as usize] +=
                //     (square.file() as i8).abs_diff(king.file() as i8) as i8 * inc;
                // // features[EDGE + piece as usize] += BitBoard::EDGES.has(square) as i8 * inc;
            }
        }

        for (i, &v) in features.iter().enumerate().filter(|&(_, &v)| v != 0) {
            entry.add_feature(0, i as i64, v as f32 * phase);
            entry.add_feature(0, (i + TOTAL_FEATURES) as i64, v as f32 * (1.0 - phase));
        }
    }
}

fn hm_feature(square: Square) -> usize {
    let square = match square.file() > File::D {
        true => square.flip_file(),
        false => square,
    };
    // let square = match square.rank() > Rank::Fourth {
    //     true => square.flip_rank(),
    //     false => square,
    // };
    square.rank() as usize * 4 + square.file() as usize
}

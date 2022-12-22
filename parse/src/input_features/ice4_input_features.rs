use cozy_chess::{BitBoard, Board, Color, File, Piece, Rank, Square};

use crate::batch::EntryFeatureWriter;

use super::InputFeatureSet;

pub struct Ice4InputFeatures;

const PAWN_PST_OFFSET: usize = 0;
const KNIGHT_PST_OFFSET: usize = PAWN_PST_OFFSET + 64;
const BISHOP_PST_OFFSET: usize = KNIGHT_PST_OFFSET + 32;
const ROOK_PST_OFFSET: usize = BISHOP_PST_OFFSET + 32;
const QUEEN_PST_OFFSET: usize = ROOK_PST_OFFSET + 32;
const KING_PST_OFFSET: usize = QUEEN_PST_OFFSET + 32;
const PASSED_PAWN_PST_OFFSET: usize = KING_PST_OFFSET + 64;
const BISHOP_PAIR_OFFSET: usize = PASSED_PAWN_PST_OFFSET + 64;
const DOUBLED_PAWN_OFFSET: usize = BISHOP_PAIR_OFFSET + 1;
const FEATURES: usize = DOUBLED_PAWN_OFFSET + 8;

const PIECE_PST_OFFSETS: [usize; 6] = [
    PAWN_PST_OFFSET,
    KNIGHT_PST_OFFSET,
    BISHOP_PST_OFFSET,
    ROOK_PST_OFFSET,
    QUEEN_PST_OFFSET,
    KING_PST_OFFSET,
];

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

        let mut features = [0i8; FEATURES];

        for &piece in &Piece::ALL {
            for square in board.pieces(piece) {
                let color = board.color_on(square).unwrap();
                let (square, inc) = match color {
                    Color::White => (square, 1),
                    Color::Black => (square.flip_rank(), -1),
                };
                let square = match piece {
                    Piece::Knight | Piece::Bishop | Piece::Rook | Piece::Queen => {
                        hm_feature(square)
                    }
                    Piece::King => square as usize,
                    Piece::Pawn => match board.king(color).file() > File::D {
                        true => square.flip_file() as usize,
                        false => square as usize,
                    },
                };
                features[PIECE_PST_OFFSETS[piece as usize] + square] += inc;
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

                let square = match board.king(color).file() > File::D {
                    true => square.flip_file(),
                    false => square,
                };

                let (sq, inc) = match color {
                    Color::White => (square as usize, 1),
                    Color::Black => (square.flip_rank() as usize, -1),
                };
                features[PASSED_PAWN_PST_OFFSET + sq] += inc;
            }
        }

        let mut white_doubled_mask = board.colored_pieces(Color::White, Piece::Pawn).0 >> 8;
        let mut black_doubled_mask = board.colored_pieces(Color::Black, Piece::Pawn).0 << 8;
        for _ in 0..6 {
            white_doubled_mask |= white_doubled_mask >> 8;
            black_doubled_mask |= black_doubled_mask << 8;
        }
        for sq in board.colored_pieces(Color::White, Piece::Pawn) & BitBoard(white_doubled_mask) {
            features[DOUBLED_PAWN_OFFSET + sq.file() as usize] += 1;
        }
        for sq in board.colored_pieces(Color::Black, Piece::Pawn) & BitBoard(black_doubled_mask) {
            features[DOUBLED_PAWN_OFFSET + sq.file() as usize] -= 1;
        }

        if board.colored_pieces(Color::White, Piece::Bishop).len() >= 2 {
            features[BISHOP_PAIR_OFFSET] += 1;
        }
        if board.colored_pieces(Color::Black, Piece::Bishop).len() >= 2 {
            features[BISHOP_PAIR_OFFSET] -= 1;
        }

        for (i, &v) in features.iter().enumerate().filter(|&(_, &v)| v != 0) {
            entry.add_feature(0, i as i64, v as f32 * phase);
            entry.add_feature(0, (i + FEATURES) as i64, v as f32 * (1.0 - phase));
        }
    }
}

fn hm_feature(square: Square) -> usize {
    let square = match square.file() > File::D {
        true => square.flip_file(),
        false => square,
    };
    square.rank() as usize * 4 + square.file() as usize
}

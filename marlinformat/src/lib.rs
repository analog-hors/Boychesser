#![no_std]

use bytemuck::{Pod, Zeroable};
use cozy_chess::{BitBoard, Board, BoardBuilder, Color, Piece, Rank, Square};

const UNMOVED_ROOK: u8 = Piece::NUM as u8;

#[derive(Copy, Clone, Debug, Pod, Zeroable)]
#[repr(C)]
pub struct PackedBoard {
    occupancy: util::U64Le,
    pieces: util::U4Array32,
    stm_ep_square: u8,
    halfmove_clock: u8,
    fullmove_number: util::U16Le,
    eval: util::I16Le,
    wdl: u8,
    extra: u8,
}

impl PackedBoard {
    pub fn pack(board: &Board, eval: i16, wdl: u8, extra: u8) -> Self {
        let occupancy = board.occupied();

        let mut pieces = util::U4Array32::default();
        for (i, sq) in occupancy.into_iter().enumerate() {
            let piece = board.piece_on(sq).unwrap();
            let color = board.color_on(sq).unwrap();

            let mut piece_code = piece as u8;
            if piece == Piece::Rook && sq.rank() == Rank::First.relative_to(color) {
                let castling_file = match board.king(color) < sq {
                    true => board.castle_rights(color).short,
                    false => board.castle_rights(color).long,
                };
                if Some(sq.file()) == castling_file {
                    piece_code = UNMOVED_ROOK;
                }
            }

            pieces.set(i, piece_code | (color as u8) << 3);
        }

        PackedBoard {
            occupancy: util::U64Le::new(occupancy.0),
            pieces,
            stm_ep_square: (board.side_to_move() as u8) << 7
                | board.en_passant().map_or(Square::NUM as u8, |f| {
                    Square::new(f, Rank::Sixth.relative_to(board.side_to_move())) as u8
                }),
            halfmove_clock: board.halfmove_clock(),
            fullmove_number: util::U16Le::new(board.fullmove_number()),
            wdl,
            eval: util::I16Le::new(eval),
            extra,
        }
    }

    pub fn unpack(&self) -> Option<(Board, i16, u8, u8)> {
        let mut builder = BoardBuilder::empty();

        let mut seen_king = [false; 2];
        for (i, sq) in BitBoard(self.occupancy.get()).into_iter().enumerate() {
            let color = Color::try_index(self.pieces.get(i) as usize >> 3)?;
            let piece_code = self.pieces.get(i) & 0b0111;
            let piece = match piece_code {
                UNMOVED_ROOK => {
                    if seen_king[color as usize] {
                        builder.castle_rights_mut(color).short = Some(sq.file());
                    } else {
                        builder.castle_rights_mut(color).long = Some(sq.file());
                    }
                    Piece::Rook
                }
                _ => Piece::try_index(piece_code as usize)?,
            };
            if piece == Piece::King {
                seen_king[color as usize] = true;
            }
            builder.board[sq as usize] = Some((piece, color));
        }

        builder.en_passant = Square::try_index(self.stm_ep_square as usize & 0b01111111);
        builder.side_to_move = Color::try_index(self.stm_ep_square as usize >> 7)?;
        builder.halfmove_clock = self.halfmove_clock;
        builder.fullmove_number = core::num::NonZeroU16::new(self.fullmove_number.get())?;

        Some((builder.build().ok()?, self.eval.get(), self.wdl, self.extra))
    }
}

mod util {
    use bytemuck::{Pod, Zeroable};

    #[derive(Copy, Clone, Debug, Default, Pod, Zeroable)]
    #[repr(transparent)]
    pub struct U64Le(u64);

    impl U64Le {
        pub fn new(v: u64) -> Self {
            U64Le(v.to_le())
        }

        pub fn get(self) -> u64 {
            u64::from_le(self.0)
        }
    }

    #[derive(Copy, Clone, Debug, Default, Pod, Zeroable)]
    #[repr(transparent)]
    pub struct U16Le(u16);

    impl U16Le {
        pub fn new(v: u16) -> Self {
            U16Le(v.to_le())
        }

        pub fn get(self) -> u16 {
            u16::from_le(self.0)
        }
    }

    #[derive(Copy, Clone, Debug, Default, Pod, Zeroable)]
    #[repr(transparent)]
    pub struct I16Le(i16);

    impl I16Le {
        pub fn new(v: i16) -> Self {
            I16Le(v.to_le())
        }

        pub fn get(self) -> i16 {
            i16::from_le(self.0)
        }
    }

    #[derive(Copy, Clone, Debug, Default, Pod, Zeroable)]
    #[repr(transparent)]
    pub struct U4Array32([u8; 16]);

    impl U4Array32 {
        pub fn get(&self, i: usize) -> u8 {
            (self.0[i / 2] >> (i % 2) * 4) & 0xF
        }

        pub fn set(&mut self, i: usize, v: u8) {
            debug_assert!(v < 0x10);
            self.0[i / 2] |= v << (i % 2) * 4;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    #[ignore]
    fn roundtrip() {
        // Grab `valid.sfens` from `cozy-chess` to run test
        for sfen in include_str!("valid.sfens").lines() {
            let board = Board::from_fen(sfen, true).unwrap();
            let packed = PackedBoard::pack(&board, 0, 0, 0);
            let (unpacked, _, _, _) = packed
                .unpack()
                .unwrap_or_else(|| panic!("Failed to unpack {}. {:#X?}", sfen, packed));
            assert_eq!(board, unpacked, "{}", sfen);
        }
    }
}

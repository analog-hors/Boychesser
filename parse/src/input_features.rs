use cozy_chess::{Board, Piece, Color};

#[repr(u8)]
#[derive(Copy, Clone)]
pub enum InputFeatureType {
    Board768,
    HalfKp,
    HalfKa,
}

impl InputFeatureType {
    pub fn from(ty: u8) -> Self {
        match ty {
            0 => Self::Board768,
            1 => Self::HalfKp,
            2 => Self::HalfKa,
            _ => panic!("Unrecognized features"),
        }
    }

    pub fn max_indices(self) -> usize {
        match self {
            Self::Board768 => 32,
            Self::HalfKp => 30,
            Self::HalfKa => 32,
        }
    }
}


pub trait InputFeature {
    fn write_indices(&self, board: Board, stm_indices: &mut [i64], nstm_indices: &mut [i64]) -> usize;
    
    fn max_features(&self) -> usize;
}

pub struct Board768;

impl InputFeature for Board768  {
    fn write_indices(&self, board: Board, stm_indices: &mut [i64], nstm_indices: &mut [i64]) -> usize {
        let stm = board.side_to_move();

        let white = board.colors(Color::White);
        let black = board.colors(Color::Black);

        let pawns = board.pieces(Piece::Pawn);
        let knights = board.pieces(Piece::Knight);
        let bishops = board.pieces(Piece::Bishop);
        let rooks = board.pieces(Piece::Rook);
        let queens = board.pieces(Piece::Queen);
        let kings = board.pieces(Piece::King);

        let array = [
            (white & pawns),
            (white & knights),
            (white & bishops),
            (white & rooks),
            (white & queens),
            (white & kings),
            (black & pawns),
            (black & knights),
            (black & bishops),
            (black & rooks),
            (black & queens),
            (black & kings),
        ];
        let mut counter = 0;
        for (index, &pieces) in array.iter().enumerate() {
            for sq in pieces {
                let (stm_index, stm_sq, nstm_index, nstm_sq) = match stm {
                    Color::White => (index, sq as usize, ((index + 5) % 10), sq as usize ^ 56),
                    Color::Black => (((index + 5) % 10), sq as usize ^ 56, index, sq as usize),
                };
                stm_indices[counter] = (stm_index * 64 + stm_sq) as i64;
                nstm_indices[counter] = (nstm_index * 64 + nstm_sq) as i64;
                counter += 1;
            }
        }
        counter
    }

    fn max_features(&self) -> usize {
        32
    }
}
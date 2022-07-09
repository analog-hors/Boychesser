use cozy_chess::{Board, Color, Piece};
use std::fs::File;
use std::io::{self, BufRead};
use std::path::Path;
use std::str::FromStr;

#[repr(u8)]
#[derive(Copy, Clone)]
pub enum InputFeatures {
    Board768,
    HalfKp,
    HalfKa,
}

impl InputFeatures {
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

#[derive(Debug, Clone, Default)]
struct Element {
    board: Board,
    cp: f32,
    wdl: f32,
}

impl Element {
    pub fn new(board: Board, cp: f32, wdl: f32) -> Self {
        Self { board, cp, wdl }
    }
}

pub struct BatchLoader {
    batch_size: usize,
    input_features: InputFeatures,

    buffer: Box<[Element]>,

    boards_stm: Box<[[i64; 2]]>,
    boards_nstm: Box<[[i64; 2]]>,
    values: Box<[f32]>,
    cp: Box<[f32]>,
    wdl: Box<[f32]>,
    file: Option<io::Lines<io::BufReader<File>>>,
    counter: usize,
}

impl BatchLoader {
    pub fn new(batch_size: usize, input_features: InputFeatures) -> Self {
        Self {
            batch_size,
            input_features,
            buffer: vec![Element::default(); batch_size].into_boxed_slice(),
            boards_stm: vec![[0; 2]; batch_size * input_features.max_indices()].into_boxed_slice(),
            boards_nstm: vec![[0; 2]; batch_size * input_features.max_indices()].into_boxed_slice(),
            values: vec![1.0; batch_size * input_features.max_indices()].into_boxed_slice(),
            cp: vec![0_f32; batch_size].into_boxed_slice(),
            wdl: vec![0_f32; batch_size].into_boxed_slice(),
            file: None,
            counter: 0,
        }
    }

    pub fn set_file(&mut self, path: &str) {
        self.file = Some(read_lines(path));
    }

    pub fn close_file(&mut self) {
        self.file = None;
    }

    pub fn read(&mut self) -> bool {
        if let Some(file) = &mut self.file {
            let mut batch_counter = 0;
            self.counter = 0;
            while batch_counter < self.batch_size {
                if let Some(Ok(line)) = file.next() {
                    let mut values = line.split(" | ");
                    let board = Board::from_str(values.next().unwrap()).unwrap();
                    let cp = values.next().unwrap().parse::<f32>().unwrap();
                    let wdl = values.next().unwrap().parse::<f32>().unwrap();
                    if cp.abs() > 3000.0 {
                        continue;
                    }
                    self.buffer[batch_counter] = Element::new(board, cp, wdl);
                    batch_counter += 1;
                } else {
                    return false;
                }
            }
        } else {
            return false;
        }
        for (index, e) in self.buffer.iter().enumerate() {
            let stm = e.board.side_to_move();

            let (cp, wdl) = match stm {
                Color::White => (e.cp, e.wdl),
                Color::Black => (-e.cp, 1.0 - e.wdl),
            };

            self.cp[index] = cp;
            self.wdl[index] = wdl;

            match self.input_features {
                InputFeatures::Board768 => Self::board_768(
                    index as i64,
                    e.board.clone(),
                    &mut self.boards_stm,
                    &mut self.boards_nstm,
                    &mut self.counter,
                ),
                InputFeatures::HalfKp => Self::half_kp(
                    index as i64,
                    e.board.clone(),
                    &mut self.boards_stm,
                    &mut self.boards_nstm,
                    &mut self.counter,
                ),
                InputFeatures::HalfKa => Self::half_ka(
                    index as i64,
                    e.board.clone(),
                    &mut self.boards_stm,
                    &mut self.boards_nstm,
                    &mut self.counter,
                ),
            };
        }
        true
    }

    fn board_768(
        batch: i64,
        board: Board,
        boards_stm: &mut [[i64; 2]],
        boards_nstm: &mut [[i64; 2]],
        counter: &mut usize,
    ) {
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

        for (index, &pieces) in array.iter().enumerate() {
            for sq in pieces {
                let (stm_index, stm_sq, nstm_index, nstm_sq) = match stm {
                    Color::White => (index, sq as usize, ((index + 5) % 10), sq as usize ^ 56),
                    Color::Black => (((index + 5) % 10), sq as usize ^ 56, index, sq as usize),
                };
                boards_stm[*counter] = [batch, (stm_index * 64 + stm_sq) as i64];
                boards_nstm[*counter] = [batch, (nstm_index * 64 + nstm_sq) as i64];
                *counter += 1;
            }
        }
    }

    fn half_kp(
        batch: i64,
        board: Board,
        boards_stm: &mut [[i64; 2]],
        boards_nstm: &mut [[i64; 2]],
        counter: &mut usize,
    ) {
        let stm = board.side_to_move();
        let w_king = board.king(Color::White) as usize;
        let b_king = board.king(Color::Black) as usize ^ 56;
        let (stm_king, nstm_king) = if stm == Color::White {
            (w_king, b_king)
        } else {
            (b_king, w_king)
        };

        let white = board.colors(Color::White);
        let black = board.colors(Color::Black);

        let pawns = board.pieces(Piece::Pawn);
        let knights = board.pieces(Piece::Knight);
        let bishops = board.pieces(Piece::Bishop);
        let rooks = board.pieces(Piece::Rook);
        let queens = board.pieces(Piece::Queen);

        let array = [
            (white & pawns),
            (white & knights),
            (white & bishops),
            (white & rooks),
            (white & queens),
            (black & pawns),
            (black & knights),
            (black & bishops),
            (black & rooks),
            (black & queens),
        ];
        for (index, &pieces) in array.iter().enumerate() {
            for sq in pieces {
                let (stm_index, stm_sq, nstm_index, nstm_sq) = match stm {
                    Color::White => (index, sq as usize, ((index + 5) % 10), sq as usize ^ 56),
                    Color::Black => (((index + 5) % 10), sq as usize ^ 56, index, sq as usize),
                };
                boards_stm[*counter] = [batch, (stm_king * 640 + stm_index * 64 + stm_sq) as i64];
                boards_nstm[*counter] =
                    [batch, (nstm_king * 640 + nstm_index * 64 + nstm_sq) as i64];

                *counter += 1;
            }
        }
    }

    fn half_ka(
        batch: i64,
        board: Board,
        boards_stm: &mut [[i64; 2]],
        boards_nstm: &mut [[i64; 2]],
        counter: &mut usize,
    ) {
        let stm = board.side_to_move();
        let w_king = board.king(Color::White) as usize;
        let b_king = board.king(Color::Black) as usize ^ 56;
        let (stm_king, nstm_king) = if stm == Color::White {
            (w_king, b_king)
        } else {
            (b_king, w_king)
        };

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
        for (index, &pieces) in array.iter().enumerate() {
            for sq in pieces {
                let (stm_index, stm_sq, nstm_index, nstm_sq) = match stm {
                    Color::White => (index, sq as usize, ((index + 6) % 12), sq as usize ^ 56),
                    Color::Black => (((index + 6) % 12), sq as usize ^ 56, index, sq as usize),
                };
                boards_stm[*counter] = [batch, (stm_king * 768 + stm_index * 64 + stm_sq) as i64];
                boards_nstm[*counter] =
                    [batch, (nstm_king * 768 + nstm_index * 64 + nstm_sq) as i64];

                *counter += 1;
            }
        }
    }

    pub fn stm_indices(&self) -> *const i64 {
        &self.boards_stm[0][0]
    }

    pub fn nstm_indices(&self) -> *const i64 {
        &self.boards_nstm[0][0]
    }

    pub fn values(&self) -> *const f32 {
        &self.values[0]
    }

    pub fn cp(&self) -> *const f32 {
        &self.cp[0]
    }

    pub fn wdl(&self) -> *const f32 {
        &self.wdl[0]
    }

    pub fn count(&self) -> u32 {
        self.counter as u32
    }
}

pub fn read_lines<P: AsRef<Path>>(filename: P) -> io::Lines<io::BufReader<File>> {
    let file = File::open(filename).unwrap();
    io::BufReader::new(file).lines()
}

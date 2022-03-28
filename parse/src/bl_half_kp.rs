use cozy_chess::{Board, Color, Piece};
use std::fs::File;
use std::io;
use std::str::FromStr;

use crate::batch_loader::{read_lines, BatchLoader};

pub const MAX_INDICES: usize = 32;

pub struct BatchLoaderHalfKp {
    batch_size: usize,
    boards_stm: Box<[[i64; 2]]>,
    boards_nstm: Box<[[i64; 2]]>,
    values: Box<[f32]>,
    cp: Box<[f32]>,
    wdl: Box<[f32]>,
    file: Option<io::Lines<io::BufReader<File>>>,
    counter: usize,
}

impl BatchLoaderHalfKp {
    pub fn new(batch_size: usize) -> Self {
        Self {
            batch_size,
            boards_stm: vec![[0; 2]; batch_size * MAX_INDICES].into_boxed_slice(),
            boards_nstm: vec![[0; 2]; batch_size * MAX_INDICES].into_boxed_slice(),
            values: vec![1.0; batch_size * MAX_INDICES].into_boxed_slice(),
            cp: vec![0_f32; batch_size].into_boxed_slice(),
            wdl: vec![0_f32; batch_size].into_boxed_slice(),
            file: None,
            counter: 0,
        }
    }
}
impl BatchLoader for BatchLoaderHalfKp {
    fn set_file(&mut self, path: &str) {
        self.file = Some(read_lines(path));
    }

    fn close_file(&mut self) {
        self.file = None;
    }

    fn read(&mut self) -> bool {
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

                    let stm = board.side_to_move();

                    let (cp, wdl) = match stm {
                        Color::White => (cp, wdl),
                        Color::Black => (-cp, 1.0 - wdl),
                    };

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
                                Color::White => {
                                    (index, sq as usize, ((index + 5) % 10), sq as usize ^ 56)
                                }
                                Color::Black => {
                                    (((index + 5) % 10), sq as usize ^ 56, index, sq as usize)
                                }
                            };
                            self.boards_stm[self.counter] = [
                                batch_counter as i64,
                                (stm_king * 640 + stm_index * 64 + stm_sq) as i64,
                            ];
                            self.boards_nstm[self.counter] = [
                                batch_counter as i64,
                                (nstm_king * 640 + nstm_index * 64 + nstm_sq) as i64,
                            ];
                            self.counter += 1;
                        }
                    }
                    self.cp[batch_counter] = cp;
                    self.wdl[batch_counter] = wdl;
                    batch_counter += 1;
                } else {
                    return false;
                }
            }
            true
        } else {
            false
        }
    }

    fn stm_indices(&self) -> *const i64 {
        &self.boards_stm[0][0]
    }

    fn nstm_indices(&self) -> *const i64 {
        &self.boards_nstm[0][0]
    }

    fn values(&self) -> *const f32 {
        &self.values[0]
    }

    fn cp(&self) -> *const f32 {
        &self.cp[0]
    }

    fn wdl(&self) -> *const f32 {
        &self.wdl[0]
    }

    fn count(&self) -> u32 {
        self.counter as u32
    }
}

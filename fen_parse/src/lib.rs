use std::ffi::CStr;
use std::fs::File;
use std::io::{self, BufRead};
use std::os::raw::c_char;
use std::path::Path;
use std::str::FromStr;

use cozy_chess::{Board, Color, Piece};

const INPUTS: usize = 768;

pub struct BatchLoader {
    batch_size: usize,
    boards_stm: Box<[[f32; INPUTS]]>,
    boards_nstm: Box<[[f32; INPUTS]]>,
    cp: Box<[f32]>,
    wdl: Box<[f32]>,
    file: Option<io::Lines<io::BufReader<File>>>,
}

impl BatchLoader {
    pub fn new(batch_size: usize) -> Self {
        Self {
            batch_size,
            boards_stm: vec![[0_f32; INPUTS]; batch_size].into_boxed_slice(),
            boards_nstm: vec![[0_f32; INPUTS]; batch_size].into_boxed_slice(),
            cp: vec![0_f32; batch_size].into_boxed_slice(),
            wdl: vec![0_f32; batch_size].into_boxed_slice(),
            file: None,
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
            let mut counter = 0;
            while counter < self.batch_size {
                if let Some(Ok(line)) = file.next() {
                    let mut values = line.split(" | ");
                    let board = Board::from_str(values.next().unwrap()).unwrap();
                    let cp = values.next().unwrap().parse::<f32>().unwrap();
                    let wdl = values.next().unwrap().parse::<f32>().unwrap();
                    if cp.abs() > 3000.0 {
                        continue;
                    }
                    let (board_stm, board_nstm, cp, wdl) = Self::to_input_vector(board, cp, wdl);

                    self.boards_stm[counter] = board_stm;
                    self.boards_nstm[counter] = board_nstm;
                    self.cp[counter] = cp;
                    self.wdl[counter] = wdl;
                    counter += 1;
                } else {
                    return false;
                }
            }
            true
        } else {
            false
        }
    }

    fn to_input_vector(
        board: Board,
        cp: f32,
        wdl: f32,
    ) -> ([f32; INPUTS], [f32; INPUTS], f32, f32) {
        let mut stm_perspective = [0_f32; INPUTS as usize];
        let mut nstm_perspective = [0_f32; INPUTS as usize];

        let stm = board.side_to_move();
        let (cp, wdl) = match stm {
            Color::White => (cp, wdl),
            Color::Black => (-cp, 1.0 - wdl),
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
                stm_perspective[stm_index * 64 + stm_sq] = 1.0;
                nstm_perspective[nstm_index * 64 + nstm_sq] = 1.0;
            }
        }
        (stm_perspective, nstm_perspective, cp, wdl)
    }
}

fn read_lines<P: AsRef<Path>>(filename: P) -> io::Lines<io::BufReader<File>> {
    let file = File::open(filename).unwrap();
    io::BufReader::new(file).lines()
}

#[no_mangle]
pub extern "C" fn new_batch_loader(batch_size: i32) -> *mut BatchLoader {
    let batch_loader = Box::new(BatchLoader::new(batch_size as usize));
    let batch_loader = Box::leak(batch_loader) as *mut BatchLoader;
    batch_loader
}

#[no_mangle]
pub extern "C" fn open_file(batch_loader: *mut BatchLoader, file: *const c_char) {
    let file = unsafe { CStr::from_ptr(file) }.to_str().unwrap();
    unsafe {
        batch_loader.as_mut().unwrap().set_file(file);
    }
}

#[no_mangle]
pub extern "C" fn close_file(batch_loader: *mut BatchLoader) {
    unsafe {
        batch_loader.as_mut().unwrap().close_file();
    }
}

#[no_mangle]
pub extern "C" fn read_batch(batch_loader: *mut BatchLoader) -> bool {
    unsafe { batch_loader.as_mut().unwrap().read() }
}

#[no_mangle]
pub extern "C" fn boards_stm(batch_loader: *mut BatchLoader) -> *mut [f32; 768] {
    unsafe { batch_loader.as_mut().unwrap().boards_stm.as_mut_ptr() }
}

#[no_mangle]
pub extern "C" fn boards_nstm(batch_loader: *mut BatchLoader) -> *mut [f32; 768] {
    unsafe { batch_loader.as_mut().unwrap().boards_nstm.as_mut_ptr() }
}

#[no_mangle]
pub extern "C" fn cp(batch_loader: *mut BatchLoader) -> *mut f32 {
    unsafe { batch_loader.as_mut().unwrap().cp.as_mut_ptr() }
}

#[no_mangle]
pub extern "C" fn wdl(batch_loader: *mut BatchLoader) -> *mut f32 {
    unsafe { batch_loader.as_mut().unwrap().wdl.as_mut_ptr() }
}

#[no_mangle]
pub extern "C" fn size() -> i32 {
    std::mem::size_of::<BatchLoader>() as i32
}

use std::ffi::CStr;
use std::fs::File;
use std::io::{self, BufRead};
use std::os::raw::c_char;
use std::path::Path;
use std::str::FromStr;

use chess::{Board, Color, Piece};

const INPUTS: usize = 768;
#[repr(C)]
pub struct BatchLoader {
    batch_size: usize,
    buckets: usize,
    boards: Vec<[f32; INPUTS]>,
    cp: Vec<f32>,
    wdl: Vec<f32>,
    mask: Vec<f32>,
    file: Option<io::Lines<io::BufReader<File>>>,
}

impl BatchLoader {
    pub fn new(batch_size: usize, buckets: usize) -> Self {
        Self {
            batch_size,
            buckets,
            boards: vec![[0_f32; INPUTS]; batch_size],
            cp: vec![0_f32; batch_size * buckets],
            wdl: vec![0_f32; batch_size * buckets],
            mask: vec![0_f32; batch_size * buckets],
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
            for val in &mut self.mask {
                *val = 0.0;
            }
            while counter < self.batch_size {
                if let Some(Ok(line)) = file.next() {
                    let mut values = line.split(" | ");
                    let board = Board::from_str(values.next().unwrap()).unwrap();
                    let cp = values.next().unwrap().parse::<f32>().unwrap();
                    let wdl = values.next().unwrap().parse::<f32>().unwrap();
                    if cp.abs() > 3000.0 {
                        continue;
                    }
                    let phase = phase(&board);
                    let bucket = (phase * self.buckets / 24).min(self.buckets - 1);
                    let (board, cp, wdl) = Self::to_input_vector(board, cp, wdl);

                    self.boards[counter] = board;
                    self.cp[counter * self.buckets + bucket] = cp;
                    self.wdl[counter * self.buckets + bucket] = wdl;
                    self.mask[counter * self.buckets + bucket] = 1.0;
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

    fn to_input_vector(board: Board, cp: f32, wdl: f32) -> ([f32; INPUTS], f32, f32) {
        let mut w_perspective = [0_f32; INPUTS as usize];

        let stm = board.side_to_move();
        let (cp, wdl) = match stm {
            Color::White => (cp, wdl),
            Color::Black => (-cp, 1.0 - wdl),
        };
        let white = *board.color_combined(Color::White);
        let black = *board.color_combined(Color::Black);

        let pawns = *board.pieces(Piece::Pawn);
        let knights = *board.pieces(Piece::Knight);
        let bishops = *board.pieces(Piece::Bishop);
        let rooks = *board.pieces(Piece::Rook);
        let queens = *board.pieces(Piece::Queen);
        let kings = *board.pieces(Piece::King);

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
                let (index, sq) = match stm {
                    Color::White => (index, sq.to_index()),
                    Color::Black => (((index + 6) % 12), sq.to_index() ^ 56),
                };
                w_perspective[index * 64 + sq] = 1.0;
            }
        }
        (w_perspective, cp, wdl)
    }
}

fn read_lines<P: AsRef<Path>>(filename: P) -> io::Lines<io::BufReader<File>> {
    let file = File::open(filename).unwrap();
    io::BufReader::new(file).lines()
}

fn phase(board: &Board) -> usize {
    (board.pieces(Piece::Knight).popcnt()
        + board.pieces(Piece::Bishop).popcnt()
        + board.pieces(Piece::Rook).popcnt() * 2
        + board.pieces(Piece::Queen).popcnt() * 4)
        .min(24) as usize
}

#[no_mangle]
pub extern "C" fn new_batch_loader(batch_size: i32, buckets: i32) -> *mut BatchLoader {
    let batch_loader = Box::new(BatchLoader::new(batch_size as usize, buckets as usize));
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
pub extern "C" fn board(batch_loader: *mut BatchLoader) -> *mut [f32; 768] {
    unsafe { batch_loader.as_mut().unwrap().boards.as_mut_ptr() }
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
pub extern "C" fn mask(batch_loader: *mut BatchLoader) -> *mut f32 {
    unsafe { batch_loader.as_mut().unwrap().mask.as_mut_ptr() }
}

#[no_mangle]
pub extern "C" fn size() -> i32 {
    std::mem::size_of::<BatchLoader>() as i32
}

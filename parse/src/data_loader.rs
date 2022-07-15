use std::{fs::File, io::Read, path::Path};

use bytemuck::Zeroable;
use cozy_chess::{Board, Color};
use marlinformat::PackedBoard;
use rayon::iter::{IndexedParallelIterator, IntoParallelRefIterator, ParallelIterator};

use crate::batch::Batch;
use crate::input_features::InputFeatureSet;

#[derive(Debug)]
pub struct AnnotatedBoard {
    board: Board,
    cp: f32,
    wdl: f32,
}

impl AnnotatedBoard {
    pub fn relative_value(&self) -> (f32, f32) {
        match self.board.side_to_move() {
            Color::White => (self.cp, self.wdl),
            Color::Black => (-self.cp, 1.0 - self.wdl),
        }
    }
}

pub struct FileReader {
    file: File,
    packed_buffer: Vec<PackedBoard>,
    board_buffer: Vec<Option<AnnotatedBoard>>,
}

impl FileReader {
    pub fn new(path: impl AsRef<Path>) -> std::io::Result<Self> {
        let file = File::open(path)?;
        Ok(Self {
            file,
            packed_buffer: vec![],
            board_buffer: vec![],
        })
    }

    fn try_fill_buffer(&mut self, chunk_size: usize) -> bool {
        self.packed_buffer.resize(chunk_size, PackedBoard::zeroed());
        let buffer = bytemuck::cast_slice_mut(&mut self.packed_buffer);
        let mut bytes_read = 0;
        loop {
            match self.file.read(&mut buffer[bytes_read..]) {
                Ok(0) => break,
                Ok(some) => bytes_read += some,
                Err(_) => break,
            }
        }
        let elems = bytes_read / std::mem::size_of::<PackedBoard>();
        self.packed_buffer.truncate(elems);

        self.packed_buffer
            .par_iter()
            .map(|packed| {
                let (board, cp, wdl) = packed.unpack()?;
                let cp = cp as f32;
                let wdl = wdl as f32 / 2.0;

                if cp.abs() > 3000.0 {
                    return None;
                }

                Some(AnnotatedBoard { board, cp, wdl })
            })
            .rev()
            .collect_into_vec(&mut self.board_buffer);
        !self.board_buffer.is_empty()
    }

    fn next_from_buffer(&mut self) -> Option<AnnotatedBoard> {
        while let Some(maybe_board) = self.board_buffer.pop() {
            if let Some(board) = maybe_board {
                return Some(board);
            }
        }
        None
    }
}

impl Iterator for FileReader {
    type Item = AnnotatedBoard;

    fn next(&mut self) -> Option<Self::Item> {
        loop {
            if let Some(board) = self.next_from_buffer() {
                return Some(board);
            }
            if !self.try_fill_buffer(32_000) {
                return None;
            }
        }
    }
}

pub fn read_batch_into<F: InputFeatureSet>(reader: &mut FileReader, batch: &mut Batch) -> bool {
    batch.clear();
    for annotated in reader.take(batch.capacity()) {
        let (cp, wdl) = annotated.relative_value();
        let entry = batch.make_entry(cp, wdl);
        F::add_features(annotated.board, entry);
    }
    batch.capacity() == batch.len()
}

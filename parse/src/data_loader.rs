use std::{
    fs::File,
    io::{BufRead, BufReader, Lines},
    path::Path,
    str::FromStr,
    vec::Drain,
};

use cozy_chess::{Board, Color};
use rayon::iter::{IndexedParallelIterator, IntoParallelRefIterator, ParallelIterator};

use crate::batch::Batch;
use crate::input_features::InputFeature;

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
    file: Lines<BufReader<File>>,
    string_buffer: Vec<String>,
    elements: Vec<AnnotatedBoard>,
}

impl FileReader {
    pub fn new(path: impl AsRef<Path>) -> std::io::Result<Self> {
        let file = File::open(path)?;
        let file = BufReader::new(file).lines();
        Ok(Self {
            file,
            string_buffer: vec![],
            elements: vec![],
        })
    }

    fn read_next_n(&mut self, mut n: usize) -> bool {
        self.string_buffer.clear();
        while let Some(Ok(line)) = self.file.next() {
            let mut split = line.split(" | ");
            split.next().unwrap();
            let cp = split.next().unwrap().parse::<f32>().unwrap();
            if cp.abs() > 3000.0 {
                continue;
            }
            self.string_buffer.push(line);
            n -= 1;
            if n == 0 {
                break;
            }
        }
        self.string_buffer
            .par_iter()
            .map(|line| {
                let mut split = line.split(" | ");
                let board = Board::from_str(split.next().unwrap()).unwrap();
                let cp = split.next().unwrap().parse::<f32>().unwrap();
                let wdl = split.next().unwrap().parse::<f32>().unwrap();
                AnnotatedBoard { board, cp, wdl }
            })
            .collect_into_vec(&mut self.elements);
        n == 0
    }

    fn left(&self) -> usize {
        self.elements.len()
    }

    fn elements(&mut self, take: usize) -> Drain<AnnotatedBoard> {
        self.elements.drain(0..take)
    }
}

pub fn read_batch_into<F: InputFeature>(reader: &mut FileReader, batch: &mut Batch) -> bool {
    if reader.left() < batch.batch_size() {
        if !reader.read_next_n(batch.batch_size()) {
            return false;
        }
    }
    batch.clear();
    for annotated in reader.elements(batch.batch_size()) {
        let (cp, wdl) = annotated.relative_value();
        let entry = batch.make_entry(cp, wdl);
        F::add_features(annotated.board, entry);
    }
    true
}

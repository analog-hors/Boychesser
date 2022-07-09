use std::{
    fs::File,
    io::{BufRead, BufReader, Lines},
    path::Path,
    str::FromStr,
    vec::Drain,
};

use cozy_chess::{Board, Color};
use rayon::iter::{IndexedParallelIterator, IntoParallelRefIterator, ParallelIterator};

use crate::input_features::{Board768, HalfKa, HalfKp, InputFeature, InputFeatureType};

pub struct Element {
    board: Board,
    cp: f32,
    wdl: f32,
}

pub struct FileReader {
    file: Lines<BufReader<File>>,
    string_buffer: Vec<String>,
    elements: Vec<Element>,
}

impl FileReader {
    fn new(file: Lines<BufReader<File>>) -> Self {
        Self {
            file,
            string_buffer: vec![],
            elements: vec![],
        }
    }

    fn read_next_n(&mut self, mut n: usize) -> bool {
        self.string_buffer.clear();
        while let Some(Ok(line)) = self.file.next() {
            let mut split = line.split(" | ");
            split.next().unwrap();
            let cp = split.next().unwrap().parse::<f32>().unwrap();
            if cp > 3000.0 {
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
                Element { board, cp, wdl }
            })
            .collect_into_vec(&mut self.elements);
        n == 0
    }

    fn left(&self) -> usize {
        self.elements.len()
    }

    fn elements(&mut self, take: usize) -> Drain<Element> {
        self.elements.drain(0..take)
    }
}

pub struct DataLoader {
    input_features: Box<dyn InputFeature>,
    file_reader: Option<FileReader>,

    batch_size: usize,

    stm_indices: Box<[[i64; 2]]>,
    nstm_indices: Box<[[i64; 2]]>,
    values: Box<[f32]>,
    cp: Box<[f32]>,
    wdl: Box<[f32]>,

    count: usize,
}

impl DataLoader {
    pub fn new(batch_size: usize, input_feature_type: InputFeatureType) -> Self {
        let input_features: Box<dyn InputFeature> = match input_feature_type {
            InputFeatureType::Board768 => Box::new(Board768),
            InputFeatureType::HalfKp => Box::new(HalfKp),
            InputFeatureType::HalfKa => Box::new(HalfKa),
        };
        Self {
            stm_indices: vec![[0; 2]; batch_size * input_features.max_features()]
                .into_boxed_slice(),
            nstm_indices: vec![[0; 2]; batch_size * input_features.max_features()]
                .into_boxed_slice(),
            values: vec![1.0; batch_size * input_features.max_features()].into_boxed_slice(),
            cp: vec![0_f32; batch_size].into_boxed_slice(),
            wdl: vec![0_f32; batch_size].into_boxed_slice(),
            input_features,
            file_reader: None,
            batch_size,
            count: 0,
        }
    }

    pub fn set_file(&mut self, path: &str) {
        self.file_reader = Some(FileReader::new(read_lines(path)));
    }

    pub fn close_file(&mut self) {
        self.file_reader = None;
    }

    pub fn read(&mut self) -> bool {
        if let Some(reader) = &mut self.file_reader {
            if reader.left() < self.batch_size {
                if !reader.read_next_n(self.batch_size) {
                    return false;
                }
            }
            self.count = 0;
            for (index, e) in reader.elements(self.batch_size).enumerate() {
                let stm = e.board.side_to_move();
                let (cp, wdl) = match stm {
                    Color::White => (e.cp, e.wdl),
                    Color::Black => (-e.cp, 1.0 - e.wdl),
                };
                self.count += self.input_features.write_indices(
                    index as i64,
                    e.board,
                    &mut self.stm_indices[self.count..],
                    &mut self.nstm_indices[self.count..],
                );
                self.cp[index] = cp;
                self.wdl[index] = wdl;
            }
            return true;
        }
        false
    }

    pub fn stm_indices(&self) -> *const i64 {
        &self.stm_indices[0][0]
    }

    pub fn nstm_indices(&self) -> *const i64 {
        &self.nstm_indices[0][0]
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
        self.count as u32
    }
}

pub fn read_lines<P: AsRef<Path>>(filename: P) -> Lines<BufReader<File>> {
    let file = File::open(filename).unwrap();
    BufReader::new(file).lines()
}

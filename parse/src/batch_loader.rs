use std::{
    fs::File,
    io::{self, BufRead},
    path::Path,
};

pub trait BatchLoader {
    fn set_file(&mut self, path: &str);

    fn close_file(&mut self);

    fn read(&mut self) -> bool;

    fn stm_indices(&self) -> *const i64;

    fn nstm_indices(&self) -> *const i64;

    fn values(&self) -> *const f32;

    fn cp(&self) -> *const f32;

    fn wdl(&self) -> *const f32;

    fn count(&self) -> u32;
}

pub fn read_lines<P: AsRef<Path>>(filename: P) -> io::Lines<io::BufReader<File>> {
    let file = File::open(filename).unwrap();
    io::BufReader::new(file).lines()
}

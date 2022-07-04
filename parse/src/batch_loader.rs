use std::{
    fs::File,
    io::{self, BufRead},
    path::Path,
};

pub fn read_lines<P: AsRef<Path>>(filename: P) -> io::Lines<io::BufReader<File>> {
    let file = File::open(filename).unwrap();
    io::BufReader::new(file).lines()
}

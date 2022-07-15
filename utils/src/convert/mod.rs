mod halfkp;
mod utils;

use std::path::PathBuf;

use halfkp::HalfKp;
use structopt::StructOpt;

#[derive(StructOpt)]
/// Convert JSON neural network file into BlackMarlin NNUE format
pub struct Options {
    /// Path to the JSON file cont
    path: PathBuf,
    #[structopt(long, short = "o", default_value = "nnue.bin")]
    output: PathBuf,
}

pub fn run(options: Options) {
    let content = std::fs::read(options.path).unwrap();

    let arch = HalfKp::from(&content);
    let bin = arch.to_bin(255.0, 64.0);
    std::fs::write(options.output, bin).unwrap();
}

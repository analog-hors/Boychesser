use std::fs::File;
use std::io::{BufRead, BufReader, BufWriter, Result, Write};
use std::path::PathBuf;

use cozy_chess::Board;
use marlinformat::PackedBoard;
use structopt::StructOpt;

/// Convert legacy text data format to marlinformat.
#[derive(StructOpt)]
pub struct Options {
    #[structopt(short, long)]
    output: PathBuf,

    txt_file: PathBuf,
}

pub fn run(options: Options) -> Result<()> {
    let input = BufReader::new(File::open(options.txt_file)?);
    let mut output = BufWriter::new(File::create(options.output)?);

    let mut had_non_integer_cp = false;
    let mut had_out_of_range_cp = false;

    for line in input.lines() {
        let line = line?;
        let _ = (|| {
            let (board, annotation) = line.split_once(" | ")?;
            let (cp, wdl) = annotation.split_once(" | ")?;

            let board: Board = board.parse().ok()?;
            let cp: f32 = cp.parse().ok()?;
            let wdl: f32 = wdl.parse().ok()?;

            if !had_non_integer_cp && cp.floor() != cp {
                println!("Warning: dataset contains non-integer centipawn values. These will be truncated.");
                had_non_integer_cp = true;
            }

            let cp = match (cp as i64).try_into() {
                Ok(v) => v,
                Err(_) => {
                    if !had_out_of_range_cp {
                        println!("Warning: dataset contains centipawn values outside the range representable by an i16. These will be saturated.");
                        had_out_of_range_cp = true;
                    }
                    match cp.is_sign_positive() {
                        true => i16::MAX,
                        false => i16::MIN,
                    }
                },
            };

            let wdl = match () {
                _ if wdl < 0.25 => 0,
                _ if wdl < 0.75 => 1,
                _ => 2
            };

            let packed = PackedBoard::pack(&board, cp, wdl, 0);
            Some(output.write_all(bytemuck::bytes_of(&packed)))
        })().transpose()?;
    }

    Ok(())
}

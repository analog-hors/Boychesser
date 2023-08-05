use std::fs::File;
use std::io::{BufReader, BufWriter, Read, Result, Seek, SeekFrom, Write};
use std::path::PathBuf;
use std::time::Instant;

use bytemuck::Zeroable;
use marlinformat::PackedBoard;
use rand::{thread_rng, Rng};
use structopt::StructOpt;

/// Randomly interleave two or more datasets.
#[derive(StructOpt)]
pub struct Options {
    #[structopt(short, long)]
    output: PathBuf,

    #[structopt(required = true, min_values = 2)]
    files: Vec<PathBuf>,
}

pub fn run(options: Options) -> Result<()> {
    let mut files: Vec<_> = options
        .files
        .iter()
        .map(|path| File::open(path))
        .collect::<Result<_>>()?;

    let mut into = File::create(options.output)?;

    let start = Instant::now();

    interleave(&mut into, &mut files, |progress, total| {
        if progress & 0xFFFFF == 0 {
            let proportion = progress as f64 / total as f64;
            print!("\r\x1B[K{progress:12}/{total} ({:4.1}%)", proportion * 100.0);
            let _ = std::io::stdout().flush();
        }
    })?;
    println!();
    println!("Done ({:.1?}).", start.elapsed());

    Ok(())
}

pub fn interleave(
    into: &mut File,
    files: &mut [File],
    mut progress: impl FnMut(u64, u64),
) -> Result<()> {
    let mut into = BufWriter::new(into);
    let mut streams = Vec::with_capacity(files.len());
    let mut total = 0;
    for file in files {
        let size_bytes = file.seek(SeekFrom::End(0))?;
        file.seek(SeekFrom::Start(0))?;
        let count = size_bytes / std::mem::size_of::<PackedBoard>() as u64;
        if count > 0 {
            streams.push((count, BufReader::new(file)));
            total += count;
        }
    }

    let mut written = 0;

    while total > 0 {
        let mut spot = thread_rng().gen_range(0..total);
        let mut index = 0;
        while streams[index].0 < spot {
            spot -= streams[index].0;
            index += 1;
        }
        let (count, reader) = &mut streams[index];

        let mut value = PackedBoard::zeroed();
        reader.read_exact(bytemuck::bytes_of_mut(&mut value))?;
        into.write_all(bytemuck::bytes_of(&value))?;

        total -= 1;
        *count -= 1;
        if *count == 0 {
            streams.swap_remove(index);
        }

        written += 1;
        progress(written, total);
    }

    Ok(())
}

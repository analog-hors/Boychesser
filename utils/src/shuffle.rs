use std::fs::File;
use std::io::{Read, Result, Seek, SeekFrom, Write};
use std::path::PathBuf;

use bytemuck::Zeroable;
use marlinformat::PackedBoard;
use rand::prelude::*;
use structopt::StructOpt;

use crate::interleave::interleave;

#[derive(StructOpt)]
/// Shuffle a dataset
pub struct Options {
    dataset: PathBuf,

    /// Overwrite the input file.
    #[structopt(long, short)]
    _in_place: bool,

    /// Output file
    #[structopt(long, short, required_unless("in-place"))]
    output: Option<PathBuf>,

    #[structopt(long, default_value = "134217728")]
    block_size: u64,
    #[structopt(long, default_value = "256")]
    group_size: u64,
}

pub fn run(options: Options) -> Result<()> {
    let output = options.output.unwrap_or_else(|| options.dataset.clone());
    let output_dir = output
        .parent()
        .expect("Could not get nominal parent directory of the oiutput file");

    let mut dataset = File::open(options.dataset)?;
    let positions = dataset.seek(SeekFrom::End(0))? / std::mem::size_of::<PackedBoard>() as u64;
    dataset.rewind()?;

    if positions <= options.block_size {
        println!("in-memory shuffle");
        let mut data = read(&mut dataset, positions)?;
        drop(dataset);
        data.shuffle(&mut thread_rng());
        let mut target = tempfile::NamedTempFile::new_in(output_dir)?;
        target.write_all(bytemuck::cast_slice(&data))?;
        target.persist(output)?;
        return Ok(());
    }

    let block_count = (positions + options.block_size - 1) / options.block_size;

    let (send, mut recv) = std::sync::mpsc::sync_channel(options.group_size as usize);

    let mut remaining = positions;
    let mut blocks_shuffled = 0;
    std::thread::spawn(move || loop {
        if remaining == 0 {
            break;
        }
        let count = remaining.min(options.block_size);
        remaining -= count;
        let mut data = read(&mut dataset, count).unwrap();
        data.shuffle(&mut thread_rng());
        let mut f = tempfile::tempfile().unwrap();
        f.write_all(bytemuck::cast_slice(&data)).unwrap();
        send.send(f).unwrap();
        blocks_shuffled += 1;
        println!("blocks: {blocks_shuffled}/{block_count}");
    });

    let mut items = block_count;
    let mut level = 0;
    loop {
        level += 1;
        items = (items + options.group_size - 1) / options.group_size;
        if items == 1 {
            break;
        }

        let (nsend, nrecv) = std::sync::mpsc::sync_channel(options.group_size as usize);
        let mut iter = recv.into_iter();
        let mut progress = 0;
        std::thread::spawn(move || loop {
            let mut files: Vec<_> = (&mut iter).take(options.group_size as usize).collect();
            if files.is_empty() {
                break;
            }
            let mut to = tempfile::tempfile().unwrap();
            interleave(&mut to, &mut files, |_, _| {}).unwrap();
            nsend.send(to).unwrap();
            progress += 1;
            println!("lvl. {level}: {progress}/{items}");
        });

        recv = nrecv;
    }

    let mut files: Vec<_> = recv.into_iter().collect();
    let mut target = tempfile::NamedTempFile::new_in(output_dir)?;
    interleave(target.as_file_mut(), &mut files, |_, _| {})?;
    target.persist(output)?;

    Ok(())
}

fn read(dataset: &mut File, count: u64) -> Result<Vec<PackedBoard>> {
    let mut boards = vec![PackedBoard::zeroed(); count as usize];
    dataset.read_exact(bytemuck::cast_slice_mut(&mut boards))?;
    Ok(boards)
}

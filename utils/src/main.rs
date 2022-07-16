use structopt::StructOpt;

mod convert;
mod interleave;
mod txt_to_data;

#[derive(StructOpt)]
pub enum Options {
    Convert(convert::Options),
    Interleave(interleave::Options),
    TxtToData(txt_to_data::Options),
}

fn main() {
    match Options::from_args() {
        Options::Convert(options) => convert::run(options),
        Options::Interleave(options) => interleave::run(options).unwrap(),
        Options::TxtToData(options) => txt_to_data::run(options).unwrap(),
    }
}

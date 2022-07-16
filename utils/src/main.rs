use structopt::StructOpt;

mod convert;
mod txt_to_data;

#[derive(StructOpt)]
pub enum Options {
    Convert(convert::Options),
    TxtToData(txt_to_data::Options),
}

fn main() {
    match Options::from_args() {
        Options::Convert(options) => convert::run(options),
        Options::TxtToData(options) => txt_to_data::run(options).unwrap(),
    }
}

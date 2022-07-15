use structopt::StructOpt;

mod convert;

#[derive(StructOpt)]
pub enum Options {
    Convert(convert::Options),
}

fn main() {
    match Options::from_args() {
        Options::Convert(options) => convert::run(options),
    }
}
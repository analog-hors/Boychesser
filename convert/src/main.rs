mod halfkp;
mod utils;
use halfkp::HalfKp;
fn main() {
    let mut path = None;
    for arg in std::env::args().into_iter().skip(1).take(1) {
        path = Some(arg);
    }
    let content = std::fs::read(format!("../nn/{}.json", path.unwrap())).unwrap();

    let arch = HalfKp::from(&content);
    let bin = arch.to_bin(255.0, 64.0);
    std::fs::write("./nnue.bin", bin).unwrap();
}

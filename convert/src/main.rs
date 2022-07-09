mod halfkp;
mod utils;
use halfkp::HalfKp;
fn main() {
    let content = std::fs::read("../nn/nn.json").unwrap();

    let arch = HalfKp::from(&content);
    let bin = arch.to_bin(255.0, 32.0);
    std::fs::write("./nnue.bin", bin).unwrap();
}

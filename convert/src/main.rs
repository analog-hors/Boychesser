mod utils;
mod halfkp;
use halfkp::HalfKp;

fn main() {
    let content = std::fs::read("../nn/nn.json").unwrap();

    let res_arch = HalfKp::from(&content);
    let bin = res_arch.to_bin(255.0, 64.0);
    std::fs::write("./nnue.bin", bin).unwrap();
}

mod bmarch;
mod resarch;
mod stmarch;
mod utils;
use bmarch::{BmArchV1, BmArchV2};
use stmarch::StmArch;

fn main() {
    let content = std::fs::read("../nn/nn.json").unwrap();

    let res_arch = StmArch::from(&content);
    let bin = res_arch.to_bin(170.0);
    std::fs::write("./nnue.bin", bin).unwrap();
}

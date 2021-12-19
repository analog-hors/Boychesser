use std::collections::HashMap;

use clap::{App, Arg};
use serde::{Deserialize, Serialize};

const INPUTS: i64 = 768;

#[derive(Debug, Clone, Serialize, Deserialize)]
struct WMap {
    pub parameters: HashMap<String, Vec<f64>>,
}

impl WMap {
    pub fn to_bytes(self, hidden: usize, buckets: usize) -> Vec<u8> {
        let mut bytes: Vec<u8> = vec![];

        let inputs: u32 = INPUTS as u32;
        let mid: u32 = hidden as u32;
        let out: u32 = buckets as u32;
        unsafe {
            bytes.extend(std::mem::transmute::<u32, [u8; 4]>(inputs));
            bytes.extend(std::mem::transmute::<u32, [u8; 4]>(mid));
            bytes.extend(std::mem::transmute::<u32, [u8; 4]>(out));
        }

        let names = [
            "input/kernel:0",
            "input/bias:0",
            "main_out/kernel:0",
            "main_out/bias:0",
            "main_res/kernel:0",
        ];

        for (index, weights) in names.iter().enumerate() {
            if let Some(weights) = self.parameters.get(&weights.to_string()).cloned() {
                if index < 4 {
                    for val in weights {
                        bytes.push(unsafe { std::mem::transmute((val * 64.0).round() as i8) });
                    }
                } else {
                    for val in weights {
                        bytes.extend(unsafe {
                            std::mem::transmute::<i32, [u8; 4]>((val * 170.0 * 64.0).round() as i32)
                        });
                    }
                }
            }
        }
        bytes
    }
}

fn main() {
    let matches = App::new("NN Converter")
        .author("Doruk S.")
        .about("Converts NNs from JSON to Black Marlin binary format")
        .arg(
            Arg::with_name("hidden")
                .value_name("HIDDEN")
                .long("hidden")
                .help("Number of hidden layer neurons in the neural network")
                .required(true)
                .takes_value(true),
        )
        .arg(
            Arg::with_name("buckets")
                .value_name("BUCKETS")
                .long("buckets")
                .help("Number of buckets in the neural network")
                .required(true)
                .takes_value(true),
        )
        .arg(
            Arg::with_name("path")
                .value_name("PATH")
                .long("path")
                .help("Path of the NN file to convert")
                .required(true)
                .takes_value(true),
        )
        .arg(
            Arg::with_name("out")
                .value_name("OUT")
                .long("out")
                .help("Path to the file where the binary NN file will be written")
                .required(true)
                .takes_value(true),
        )
        .get_matches();

    let hidden = matches
        .value_of("hidden")
        .unwrap()
        .parse::<usize>()
        .unwrap();

    let buckets = matches
        .value_of("buckets")
        .unwrap()
        .parse::<usize>()
        .unwrap();

    let path = matches.value_of("path").unwrap();
    let out_path = matches.value_of("out").unwrap();

    let w: WMap = serde_json::from_str(&std::fs::read_to_string(path).unwrap()).unwrap();

    let bytes = w.to_bytes(hidden, buckets);
    std::fs::write(out_path, bytes).unwrap();
}

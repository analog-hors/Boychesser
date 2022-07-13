# marlinflow
Neural Network training repository for the Black Marlin chess engine

# Requirements
- Python 3
- Cargo(Rust)
- Numpy
- PyTorch

# Fen Parse
`fen_parse` requires files to be saved as following:
```
<fen0> | <eval0> | <wdl0>
<fen1> | <eval1> | <wdl1>
```

# Usage
Clone the project.
```bash
cd parse
cargo build --release
```

locate the .so/.dylib/.dll in target/release and move it to project root.

```bash
mkdir nn
```

Run main.py with the proper command line arguments
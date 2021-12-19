# marlinflow
Neural Network training repository for the Black Marlin chess engine

# Requirements
- Python 3
- Cargo(Rust)
- Numpy
- Tensorflow
- Tensorflow Addons

# Fen Parse
`fen_parse` requires files to be saved as following:
```
<fen0> | <eval0> | <wdl0>
<fen1> | <eval1> | <wdl1>
```

# Usage
Clone the project and run `python main.py --help` (Replace with `python` with `python3` if needed).
When run from the project root, `main.py` will automatically compile `fen_parse` and move it to the project root if it isn't already there.
It is recommended to use the `--recompile` argument if `fen_parse` has been updated.

Neural Networks will be saved to JSON files after each epoch.
These JSON files can later be converted into the [Black Marlin](https://github.com/dsekercioglu/blackmarlin) Neural Network format using `convert`.

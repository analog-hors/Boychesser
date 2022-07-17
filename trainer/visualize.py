from __future__ import annotations
import pathlib

import re
import sys

from matplotlib import pyplot as plt

MATCH = re.compile(r"epoch\s([0-9]+):\s([0-9]+.[0-9]+)")


def _read_file(path: str) -> tuple[list[int], list[float]]:
    epochs: list[int] = []
    losses: list[float] = []
    with open(path) as train_log:
        for line in train_log:
            match = re.search(MATCH, line)
            if match is None:
                continue
            epochs.append(int(match.group(1)))
            losses.append(float(match.group(2)))
    return epochs, losses


def main():
    files = [arg for arg in sys.argv if arg.endswith(".txt")]
    for f in files:
        epochs, losses = _read_file(f)
        name = pathlib.Path(f).name.split(".")[-2]
        plt.plot(epochs, losses, label=name)
    plt.legend()
    plt.show()


if __name__ == "__main__":
    main()

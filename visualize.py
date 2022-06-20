import sys
from matplotlib import pyplot as plt
import re

MATCH = re.compile("epoch\s([0-9]+):\s([0-9]+.[0-9]+)")


def _read_file(path: str) -> tuple[list[int], list[float]]:
    epochs: list[int] = []
    losses: list[float] = []
    with open(path) as train_log:
        for line in train_log.readlines():
            match = re.search(MATCH, line)
            if match is None:
                continue
            epochs.append(int(match.group(1)))
            losses.append(float(match.group(2)))
    return epochs, losses


def main():
    files = []
    for arg in sys.argv:
        if arg.endswith(".txt"):
            files.append(arg)
    for f in files:
        epochs, losses = _read_file(f)
        plt.plot(epochs[50:], losses[50:])

    plt.show()


if __name__ == "__main__":
    main()

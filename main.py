import argparse
import json
import time
from glob import glob
from typing import Union

import numpy as np
from dataloader import ParserFileReader, BatchLoader, InputFeatureSet
from model import NnBoard768, NnHalfKA, NnHalfKP
import torch

import pathlib
from trainlog import TrainLog


EPOCH_FENS = 1_000_000
SCALE = 400


WDL = 0.1  # 0.0 <= WDL <= 1.0
DATADIR = "train/syzygy"
MODEL = "nn"
TRAIN_ID = "baseline"

DEVICE = torch.device("cuda:0" if torch.cuda.is_available() else "cpu")


def train(
    model,
    optimizer,
    dataloader: BatchLoader,
    wdl,
    epochs,
    save_epochs,
    epochs_iter,
    lr_drop: Union[None, int] = None,
    train_log: Union[None, TrainLog] = None,
):
    running_loss = 0.0
    start_time = time.time()
    iterations = 0
    fens = 0

    epoch = 0

    while True:
        optimizer.zero_grad()
        batch = dataloader.read_batch(DEVICE)
        prediction = model(batch)
        expected = torch.sigmoid(batch.cp / SCALE) * (1 - wdl) + batch.wdl * wdl

        loss = torch.mean((prediction - expected) ** 2)
        loss.backward()
        optimizer.step()

        running_loss += loss.item()
        iterations += 1
        fens += batch.size

        if iterations % epochs_iter == 0:
            if train_log is not None:
                train_log.update(running_loss / epochs_iter)
                train_log.save()
            epoch += 1
            print(f"epoch {epoch}")
            print(f"running loss: {running_loss / epochs_iter}")
            print(f"FEN/s: {fens / (time.time() - start_time)}")

            running_loss = 0
            start_time = time.time()
            iterations = 0
            fens = 0

            if epoch == lr_drop:
                optimizer.param_groups[0]["lr"] *= 0.1

            if epoch % save_epochs == 0:
                param_map = {}
                for name, param in model.named_parameters():
                    param_map[name] = param.detach().cpu().numpy().T.tolist()
                with open(f"nn/{MODEL}.json", "w") as json_file:
                    json.dump(param_map, json_file)
        if epoch >= epochs:
            return


def main():

    parser = argparse.ArgumentParser(description="")

    parser.add_argument(
        "--data-root", type=str, help="Root directory of the data files"
    )
    parser.add_argument("--train-id", type=str, help="ID to save train logs with")
    parser.add_argument("--lr", type=float, help="Initial learning rate")
    parser.add_argument("--epochs", type=int, help="Epochs to train for")
    parser.add_argument("--batch-size", type=int, default=16384, help="Batch size")
    parser.add_argument("--wdl", type=float, default=0.0, help="WDL weight to be used")
    parser.add_argument(
        "--save-epochs",
        type=int,
        default=100,
        help="How often the program will save the network",
    )
    parser.add_argument(
        "--lr-drop",
        type=int,
        default=None,
        help="The epoch learning rate will be dropped",
    )
    args = parser.parse_args()

    train_log = TrainLog(args.train_id)

    model = NnHalfKP(128).to(DEVICE)

    data_path = pathlib.Path(args.data_root)
    paths = [str(path) for path in data_path.glob(f"*.txt")]
    dataloader = BatchLoader(paths, model.input_feature_set(), args.batch_size)

    optimizer = torch.optim.Adam(model.parameters(), lr=args.lr)

    train(
        model,
        optimizer,
        dataloader,
        args.wdl,
        args.epochs,
        args.save_epochs,
        EPOCH_FENS // args.batch_size,
        lr_drop=args.lr_drop,
        train_log=train_log,
    )


if __name__ == "__main__":
    main()

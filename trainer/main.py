from __future__ import annotations

import argparse
import json
import os
import pathlib

from dataloader import BatchLoader, BucketingScheme
from model import (
    NnBoard768Cuda,
    NnBoard768,
    NnHalfKA,
    NnHalfKACuda,
    NnHalfKP,
    NnHalfKPCuda,
)
from time import time
from to_frozenight import to_frozenight

import torch

DEVICE = torch.device("cuda:0" if torch.cuda.is_available() else "cpu")


class WeightClipper:
    def __init__(self, frequency=1):
        self.frequency = frequency

    def __call__(self, module):
        if hasattr(module, "weight"):
            w = module.weight.data
            w = w.clamp(-1.98, 1.98)
            module.weight.data = w


def train(
    model: torch.nn.Module,
    optimizer: torch.optim.Optimizer,
    dataloader: BatchLoader,
    wdl: float,
    scale: float,
    epochs: int,
    save_epochs: int,
    lr_drop: int | None = None,
    dryrun = False
) -> None:
    clipper = WeightClipper()
    running_loss = torch.zeros((1,), device=DEVICE)
    start_time = time()
    iterations = 0

    fens = 0
    epoch = 0

    while epoch < epochs:
        new_epoch, batch = dataloader.read_batch(DEVICE)
        if new_epoch:
            epoch += 1
            if epoch == lr_drop:
                optimizer.param_groups[0]["lr"] *= 0.1
            print(
                f"epoch: {epoch}",
                f"loss: {running_loss.item() / iterations:.4g}",
                f"pos/s: {fens / (time() - start_time):.0f}",
                sep="\t",
                flush=True,
            )

            running_loss = torch.zeros((1,), device=DEVICE)
            start_time = time()
            iterations = 0
            fens = 0

            if epoch % save_epochs == 0 and not dryrun:
                param_map = {
                    name: param.detach().cpu().numpy().tolist()
                    for name, param in model.named_parameters()
                }
                with open(f"nn/{epoch}.json", "w") as json_file:
                    json.dump(to_frozenight(param_map), json_file)

        if not dryrun:
            optimizer.zero_grad()
            prediction = model(batch)
            expected = torch.sigmoid(batch.cp / scale) * (1 - wdl) + batch.wdl * wdl

            loss = torch.mean((prediction - expected) ** 2)
            loss.backward()
            optimizer.step()
            model.apply(clipper)

            with torch.no_grad():
                running_loss += loss
        iterations += 1
        fens += batch.size


def main():

    parser = argparse.ArgumentParser(description="")

    parser.add_argument(
        "--data-root", type=str, help="Root directory of the data files"
    )
    parser.add_argument("--lr", type=float, help="Initial learning rate")
    parser.add_argument("--epochs", type=int, help="Epochs to train for")
    parser.add_argument("--batch-size", type=int, default=16384, help="Batch size")
    parser.add_argument("--wdl", type=float, default=0.0, help="WDL weight to be used")
    parser.add_argument("--scale", type=float, help="WDL weight to be used")
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

    assert args.scale is not None

    model = NnBoard768(32, BucketingScheme.MODIFIED_MATERIAL).to(DEVICE)

    data_path = pathlib.Path(args.data_root)
    paths = list(map(str, data_path.glob("*.bin")))
    dataloader = BatchLoader(paths, model.input_feature_set(), model.bucketing_scheme, args.batch_size)

    optimizer = torch.optim.Adam(model.parameters(), lr=args.lr)

    train(
        model,
        optimizer,
        dataloader,
        args.wdl,
        args.scale,
        args.epochs,
        args.save_epochs,
        lr_drop=args.lr_drop,
        dryrun=False
    )


if __name__ == "__main__":
    main()

from __future__ import annotations

import argparse
import json
import os
import pathlib
import subprocess

from dataloader import BatchLoader, BucketingScheme
from model import Ice4Model
from time import time

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
    models: list[torch.nn.Module],
    optimizer: torch.optim.Optimizer,
    dataloader: BatchLoader,
    wdl: float,
    scale: float,
    epochs: int,
    save_epochs: int,
    lr_drop: int | None = None,
    lr_decay: float = 1.0,
    nndir: str = "nn"
) -> None:
    clipper = WeightClipper()
    running_loss = [torch.zeros((1,), device=DEVICE) for _ in models]
    start_time = time()
    iterations = 0

    fens = 0
    epoch = 0

    while epoch < epochs:
        new_epoch, batch = dataloader.read_batch(DEVICE)
        if new_epoch:
            epoch += 1
            optimizer.param_groups[0]["lr"] *= lr_decay
            if epoch == lr_drop:
                optimizer.param_groups[0]["lr"] *= 0.1
            print(f"epoch: {epoch}", end="\t")
            print(f"losses:", end="\t")
            for loss in running_loss:
                print(f"{loss.item() / iterations:.4g}", end="\t")
            print(f"pos/s: {fens / (time() - start_time):.0f}", flush=True)

            running_loss = [torch.zeros((1,), device=DEVICE) for _ in models]
            start_time = time()
            iterations = 0
            fens = 0

            if epoch % save_epochs == 0:
                for i, model in enumerate(models):
                    param_map = {
                        name: param.detach().cpu().numpy().tolist()
                        for name, param in model.named_parameters()
                    }
                    with open(f"{nndir}/{i}-{epoch}.json", "w") as json_file:
                        json.dump(param_map, json_file)

        expected = torch.sigmoid(batch.cp / scale) * (1 - wdl) + batch.wdl * wdl
        optimizer.zero_grad()
        for model, run_loss in zip(models, running_loss):
            prediction = model(batch)

            loss = torch.mean(torch.abs(prediction - expected) ** 2.6)
            loss.backward()

            with torch.no_grad():
                run_loss += loss
        optimizer.step()
        # for model in models:
        #     model.apply(clipper)

        iterations += 1
        fens += batch.size


def main():

    parser = argparse.ArgumentParser(description="")

    parser.add_argument(
        "--data", type=str, help="The data file"
    )
    parser.add_argument("--nndir", type=str, default="nn", help="")
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
    parser.add_argument(
        "--models",
        type=int,
        default=1,
        help="The number of models to train in parallel"
    )
    parser.add_argument(
        "--lr-decay",
        type=float,
        default=1,
        help="Factor to multiply LR by every epoch"
    )
    args = parser.parse_args()

    assert args.scale is not None

    models = []
    for i in range(args.models):
        models.append(Ice4Model().to(DEVICE))

    dataloader = BatchLoader(
        lambda: [
#            subprocess.run(
#                ["./marlinflow-utils", "shuffle", "-i", args.data],
#                stdout=subprocess.DEVNULL
#            ),
            args.data
        ][-1],
        models[0].input_feature_set(),
        models[0].bucketing_scheme,
        args.batch_size
    )

    optimizer = torch.optim.Adam([
        param for model in models for param in model.parameters()
    ], lr=args.lr)

    train(
        models,
        optimizer,
        dataloader,
        args.wdl,
        args.scale,
        args.epochs,
        args.save_epochs,
        lr_drop=args.lr_drop,
        lr_decay=args.lr_decay,
        nndir=args.nndir,
    )


if __name__ == "__main__":
    main()

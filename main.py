import json
import time
from typing import Union

import numpy as np
from dataloader import BatchLoader
from model import NnBoard768, NnHalfKA, NnHalfKP
import torch

# import tensorflow_addons as tfa
from trainlog import TrainLog


BATCH_SIZE = 16384
EPOCH_ITERS = 1_000_000 // BATCH_SIZE
SCALE = 400


WDL = 0.1  # 0.0 <= WDL <= 1.0
DATADIR = "train/syzygy"
MODEL = "nn"
TRAIN_ID = "baseline"

DEVICE = torch.device("cuda:0" if torch.cuda.is_available() else "cpu")


def train(
    model,
    optimizer,
    dataloader,
    epochs=1600,
    save_epochs=20,
    lr_drop: Union[None, int] = None,
    train_log: Union[None, TrainLog] = None,
):
    running_loss = 0.0
    start_time = time.time()
    iterations = 0

    epoch = 0

    while True:
        optimizer.zero_grad()
        batch = dataloader.get_next_batch()
        prediction = model(batch)
        expected = torch.sigmoid(batch.cp / SCALE) * (1 - WDL) + batch.wdl * WDL

        loss = torch.mean((prediction - expected) ** 2)
        loss.backward()
        optimizer.step()

        running_loss += loss.item()
        iterations += 1

        if iterations % EPOCH_ITERS == 0:
            if train_log is not None:
                train_log.update(running_loss / EPOCH_ITERS)
                train_log.save()
            epoch += 1
            print(f"epoch {epoch}")
            print(f"running loss: {running_loss / EPOCH_ITERS}")
            print(f"FEN/s: {(BATCH_SIZE * iterations) / (time.time() - start_time)}")

            running_loss = 0
            start_time = time.time()
            iterations = 0

            if epoch == lr_drop:
                optimizer.learning_rate = 1e-4

            if epoch % save_epochs == 0:
                param_map = {}
                for name, param in model.named_parameters():
                    param_map[name] = param.detach().cpu().numpy().T.tolist()
                with open(f"nn/{MODEL}.json", "w") as json_file:
                    json.dump(param_map, json_file)
        if epoch >= epochs:
            return


def main():
    train_log = TrainLog(TRAIN_ID)

    dataloader = BatchLoader(BATCH_SIZE, "HalfKP", DEVICE)
    dataloader.add_directory("train/syzygy")
    model = NnHalfKP(128).to(DEVICE)

    optimizer = torch.optim.Adam(model.parameters(), lr=1e-3)

    train(
        model,
        optimizer,
        dataloader,
        epochs=2800,
        save_epochs=100,
        lr_drop=700,
        train_log=train_log,
    )


if __name__ == "__main__":
    main()

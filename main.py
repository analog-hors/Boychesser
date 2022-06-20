import json
import time
from typing import Union
from dataloader import BatchLoader
from model import NnBasic, NnRes
import tensorflow as tf

# import tensorflow_addons as tfa
from trainlog import TrainLog


BATCH_SIZE = 16384
EPOCH_ITERS = 1_000_000 // BATCH_SIZE
SCALE = 400


WDL = 0.1  # 0.0 <= WDL <= 1.0
DEVICE = "GPU"  # "CPU" or "GPU"
DATADIR = "train/syzygy"
MODEL = "nn"
TRAIN_ID = "exp1"


def train(
    model,
    optimizer,
    dataloader,
    epochs=1600,
    save_epochs=20,
    lr_drop: Union[None, int] = None,
    train_log: Union[None, TrainLog] = None,
):
    @tf.function
    def backprop(cp, wdl, boards_stm, boards_nstm, v_boards_stm, v_boards_nstm):
        with tf.GradientTape() as tape:
            expected = tf.sigmoid(cp / SCALE) * (1 - WDL) + wdl * WDL
            prediction = model(
                (
                    boards_stm,
                    boards_nstm,
                    v_boards_stm,
                    v_boards_nstm,
                )
            )
            loss_value = tf.reduce_mean(tf.square(expected - prediction))

            grads = tape.gradient(loss_value, model.trainable_weights)
            optimizer.apply_gradients(zip(grads, model.trainable_weights))
        return loss_value

    running_loss = 0.0
    start_time = time.time()
    iterations = 0

    epoch = 0

    while True:
        batch = dataloader.get_next_batch()
        try:
            loss_value = backprop(
                batch.cp,
                batch.wdl,
                batch.boards_stm,
                batch.boards_nstm,
                batch.v_boards_stm,
                batch.v_boards_nstm,
            )
        except KeyboardInterrupt:
            exit()
        except BaseException:
            print("WARNING: Unresolved Tensorflow Issue")
            print("Skipping training step")
            continue
        running_loss += loss_value
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
                for variables in model.trainable_variables:
                    param_map[variables.name] = variables.numpy().tolist()
                with open(f"nn/{MODEL}.json", "w") as json_file:
                    json.dump(param_map, json_file)
                model.save_weights(f"./nn/{MODEL}")
        if epoch >= epochs:
            return


def main():
    with tf.device(f"/{DEVICE}:0"):

        train_log = TrainLog(TRAIN_ID)

        dataloader = BatchLoader(BATCH_SIZE)
        dataloader.add_directory("train/syzygy")
        model = NnRes(128)

        optimizer = tf.keras.optimizers.Adam(learning_rate=1e-3)

        train(
            model,
            optimizer,
            dataloader,
            epochs=1400,
            save_epochs=20,
            lr_drop=700,
            train_log=train_log,
        )


if __name__ == "__main__":
    main()

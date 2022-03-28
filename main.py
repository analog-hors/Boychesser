import argparse
import json
import time
from dataloader import BatchLoader
from model import NnBasic
import tensorflow as tf
import tensorflow_addons as tfa

BATCH_SIZE = 16384
EPOCH_ITERS = 1_000
SCALE = 400
WDL = 0.1


def train(model, optimizer, dataloader, save_epochs=30):

    running_loss = 0.0
    start_time = time.time()
    iterations = 0

    epoch = 0

    while True:
        batch = dataloader.get_next_batch()
        expected = tf.sigmoid(batch.cp / SCALE) * (1 - WDL) + batch.wdl * WDL

        with tf.GradientTape() as tape:

            prediction = model(
                (
                    batch.boards_stm,
                    batch.boards_nstm,
                    batch.v_boards_stm,
                    batch.v_boards_nstm,
                )
            )
            loss_value = tf.reduce_mean(tf.square(expected - prediction))
        grads = tape.gradient(loss_value, model.trainable_weights)
        optimizer.apply_gradients(zip(grads, model.trainable_weights))

        running_loss += loss_value
        iterations += 1

        current_epoch = (iterations * BATCH_SIZE) // EPOCH_ITERS
        if current_epoch != epoch:
            print(f"running loss: {running_loss / iterations}")
            print(f"FEN/s: {(BATCH_SIZE * iterations) / (time.time() - start_time)}")
            running_loss = 0

            epoch = current_epoch

            if (epoch + 1) % save_epochs == 0:
                param_map = {}
                for variables in model.trainable_variables:
                    param_map[variables.name] = variables.numpy().tolist()
                with open("nn/nn.json", "w") as json_file:
                    json.dump(param_map, json_file)


def main():
    with tf.device("/CPU:0"):
        dataloader = BatchLoader(BATCH_SIZE)
        model = NnBasic(256)

        optimizer = tfa.optimizers.AdaBelief(
            learning_rate=1e-3, rectify=False, amsgrad=True
        )

        dataloader.set_directory("./train/additional_pylon")
        train(model, optimizer, dataloader, save_epochs=250)


if __name__ == "__main__":
    main()

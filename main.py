import argparse
import json
import time
from dataloader import BatchLoader
from model import NnBasic
import tensorflow as tf
import tensorflow_addons as tfa


BATCH_SIZE = 16384
EPOCH_ITERS = 1_000_000
SCALE = 170
WDL = 0.1


def main():
    dataloader = BatchLoader(BATCH_SIZE)
    model = NnBasic(256)
    model.build(input_shape=(2, BATCH_SIZE, 768))
    model.summary()
    optimizer = tf.keras.optimizers.Adam(learning_rate=1e-3)

    dataloader.set_directory("./train/shuffled")

    running_loss = 0.0
    start_time = time.time()
    iterations = 0

    epoch = 0

    while True:
        batch = dataloader.get_next_batch()

        expected = tf.sigmoid(batch.cp / SCALE) * (1 - WDL) + batch.wdl * WDL

        with tf.GradientTape() as tape:
            boards_stm = tf.expand_dims(batch.boards_stm, 0)
            boards_nstm = tf.expand_dims(batch.boards_nstm, 0)
            prediction = model(tf.concat([boards_stm, boards_nstm], 0))
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
            param_map = {}

            for variables in model.trainable_variables:
                param_map[variables.name] = variables.numpy().tolist()
            with open("nn/nn.json", "w") as json_file:
                json.dump(param_map, json_file)
            model.save_weights('nn/nn.ckpts')


if __name__ == "__main__":
    main()

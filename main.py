import os
from typing import Tuple
import tensorflow as tf
import numpy as np
import math
import time
import sys
import ctypes
import json
import tensorflow_addons as tfa
import argparse


INPUTS = 768
OUT = 1

PRINT_ITERS = 100


def is_dll(name: str):
    return name.endswith((".dll", ".dylib", ".so"))


def execute_os(command: str, yes: bool) -> bool:
    if yes:
        approval = "y"
    else:
        approval = input(
            f"\nDo you approve the execution of \n{command}\n(y/n)\n").lower()
    if approval == "y":
        code = os.system(command)
        if code != 0:
            print(command)
            exit(code)
        return True
    else:
        return False


def get_fen_parser(yes: bool) -> str:
    FAIL_MESSAGE = "Failed to automatically compile fen_parser. Compile fen_parser manually and move the dynamic library in target/release to the project root"
    file_names = os.listdir()
    for file_name in file_names:
        if file_name.startswith("libfen_parse"):
            dylib_name = file_name
            break
    else:
        dylib_name = None
    if RECOMPILE:
        execute_os(f"rm {dylib_name}", yes)

    if dylib_name is None or RECOMPILE:
        execute_os("cd ./fen_parse && cargo build --release", yes)
        dylib_directory = "./fen_parse/target/release"
        file_names = os.listdir(dylib_directory)
        for file_name in file_names:
            if is_dll(file_name) and file_name.startswith("libfen_parse"):
                dylib_name = file_name
                break
        else:
            dylib_name = None
        if dylib_name is None:
            print(FAIL_MESSAGE)
            exit(1)
        execute_os(f"cp {dylib_directory}/{dylib_name} ./{dylib_name}", yes)
    return dylib_name


def get_next_batch(batch_loader):
    inputs = batch_memory(batch_loader)
    cp = cp_values(batch_loader)
    wdl = wdl_memory(batch_loader)
    mask = mask_memory(batch_loader)

    inputs = tf.constant(
        np.ctypeslib.as_array(inputs, shape=(BATCH_SIZE, INPUTS)))

    cp = tf.constant(np.ctypeslib.as_array(
        cp, shape=(BATCH_SIZE, BUCKETS)))
    wdl = tf.constant(
        np.ctypeslib.as_array(wdl, shape=(BATCH_SIZE, BUCKETS)))
    mask = tf.constant(
        np.ctypeslib.as_array(mask, shape=(BATCH_SIZE, BUCKETS)))
    return (inputs, cp, wdl, mask)


def train_loop(model, optimizer, batch_loader):
    counter = 0
    data_points = 0

    accumulated_loss = 0

    while True:
        start = time.time()
        if read_batch(batch_loader):
            inputs, evals, wdl, mask = get_next_batch(batch_loader)
        else:
            break

        train_val = tf.sigmoid(evals / SCALE) * (1 - WDL) + wdl * WDL

        with tf.GradientTape() as tape:
            prediction = model(inputs)
            loss_value = tf.reduce_mean(
                tf.square(train_val - prediction) * mask)

        grads = tape.gradient(loss_value, model.trainable_weights)
        optimizer.apply_gradients(zip(grads, model.trainable_weights))
        accumulated_loss += loss_value
        counter += 1
        data_points += BATCH_SIZE

        if (counter + 1) % PRINT_ITERS == 0:
            print(f"data points {data_points}")
            print(f"batch {counter} loss {loss_value.numpy()}")
            print("FEN/s", BATCH_SIZE / (time.time() - start))
    return accumulated_loss / counter, data_points / (time.time() - start)


class FeatureTransformer(tf.keras.Model):
    def __init__(self):
        super(FeatureTransformer, self).__init__()
        self.model = tf.keras.Sequential(
            [
                tf.keras.layers.InputLayer(INPUTS),
                tf.keras.layers.Dense(HIDDEN, name="input"),
                tf.keras.layers.ReLU(
                    max_value=1.0, threshold=0.0
                ),
            ]
        )

    def call(self, inputs):
        return self.model(inputs)


class ShallowNet(tf.keras.Model):
    def __init__(self, name):
        super(ShallowNet, self).__init__()
        self.model = tf.keras.Sequential(
            [
                tf.keras.layers.InputLayer(INPUTS),
                tf.keras.layers.Dense(
                    BUCKETS, name=f"{name}_res", use_bias=False),
            ]
        )

    def call(self, inputs):
        return self.model(inputs)


class OutLayer(tf.keras.Model):
    def __init__(self, name):
        super(OutLayer, self).__init__()
        self.model = tf.keras.Sequential(
            [
                tf.keras.layers.InputLayer(HIDDEN),
                tf.keras.layers.Dense(
                    BUCKETS, name=f"{name}_out"),
            ]
        )

    def call(self, inputs):
        return self.model(inputs)


class MainArch(tf.keras.Model,):

    def __init__(self):
        super(MainArch, self).__init__()
        self.ft = FeatureTransformer()

        self.out = OutLayer("main")
        if RES:
            self.res = ShallowNet("main")

    def call(self, inputs):
        x = self.ft(inputs)
        out = self.out(x)
        if RES:
            return tf.sigmoid(out + self.res(inputs))
        else:
            return tf.sigmoid(out)


def main():

    parser = argparse.ArgumentParser(
        description="Train Neural Networks for Chess Engines", formatter_class=argparse.ArgumentDefaultsHelpFormatter)

    # Required
    parser.add_argument(
        "--name", type=str, required=True, help="Name of the file the network will be saved to")
    parser.add_argument(
        "--dir", type=str, required=True, help="The directory where all the data files take place")
    parser.add_argument(
        "--scale", type=float, required=True, help="The Neural Network's output will be multiplied by this scale when used in the engine")

    parser.add_argument(
        "--out", type=str, help="The directory to save neural network JSON files to", default="./")
    parser.add_argument(
        "--wdl", default=0, type=float, help="Win Draw Loss weight while training the neural network, calculated as eval * (1 - weight) + wdl * weight")
    parser.add_argument(
        "--epochs", default=0, type=int, help="Epochs to train the neural network for, 0 is infinite")
    parser.add_argument("--res", action="store_true",
                        help="Enables residual layers/skipped connections in the network")
    parser.add_argument("--buckets", default=1, type=int,
                        help="How many game phase buckets to use, phase is calculated as [1, 1, 2, 4] * [N, B, R, Q]")
    parser.add_argument("--hidden", default=128, type=int,
                        help="Number of hidden layer neurons")
    parser.add_argument("--batchsize", default=16384,
                        type=int, help="Batch size to use")
    parser.add_argument("--recompile", action="store_true",
                        help="Whether to recompile the fen parser dynamic library or not")
    parser.add_argument("--y", action="store_true",
                        help="Do not ask when running shell scripts")

    args = parser.parse_args()

    assert args.epochs >= 0, "Epochs must be non-negative"
    assert 0 <= args.wdl <= 1, "WDL must be in [0, 1] range"
    assert args.scale > 0, "Scale must be positive"
    assert args.batchsize > 0, "Batch size must be positive"
    assert 0 < args.buckets <= 25, "Bucket size must be positive and be less than the amount of possible game phase values"

    global NN_NAME
    global DATA_DIR
    global SCALE
    global OUT_DIR
    global WDL
    global EPOCHS
    global HIDDEN
    global BATCH_SIZE
    global RECOMPILE
    global RES
    global BUCKETS

    NN_NAME = args.name
    DATA_DIR = args.dir
    SCALE = args.scale
    OUT_DIR = args.out
    WDL = args.wdl
    EPOCHS = args.epochs
    HIDDEN = args.hidden
    BATCH_SIZE = args.batchsize
    RECOMPILE = args.recompile
    RES = args.res
    BUCKETS = args.buckets

    dllpath = get_fen_parser(args.y)
    dll = ctypes.cdll.LoadLibrary(f"./{dllpath}")

    global new_batch_loader
    global close_file
    global open_file
    global read_batch
    global batch_memory
    global cp_values
    global wdl_memory
    global mask_memory
    global batch_loader

    new_batch_loader = dll.new_batch_loader
    new_batch_loader.restype = ctypes.POINTER(ctypes.c_uint64)
    close_file = dll.close_file
    open_file = dll.open_file
    open_file.restype = ctypes.c_bool
    read_batch = dll.read_batch
    read_batch.restype = ctypes.c_bool

    batch_memory = dll.board
    batch_memory.restype = ctypes.POINTER(ctypes.c_float)

    cp_values = dll.cp
    cp_values.restype = ctypes.POINTER(ctypes.c_float)

    wdl_memory = dll.wdl
    wdl_memory.restype = ctypes.POINTER(ctypes.c_float)

    mask_memory = dll.mask
    mask_memory.restype = ctypes.POINTER(ctypes.c_float)

    batch_loader = new_batch_loader(
        ctypes.c_int32(BATCH_SIZE), ctypes.c_int32(BUCKETS))

    if not batch_loader:
        print("Batch loader initialization failed")
        return
    files = []
    directories = [DATA_DIR]
    for directory in directories:
        for text_file in os.listdir(directory):
            if text_file.endswith(".txt"):
                files.append(bytes(f"{directory}/{text_file}", "utf-8"))
    errors = []

    model = MainArch()
    model.build(input_shape=(BATCH_SIZE, INPUTS))
    model.summary()

    print("Initializing Optimizer...")
    optimizer = tfa.optimizers.AdaBelief(
        learning_rate=1e-3, amsgrad=True, rectify=False)
    print("Starting the training process...")

    param_map = {}
    for variables in model.trainable_variables:
        param_map[variables.name] = variables[0].numpy().flatten().tolist()
    params = json.dumps({"parameters": param_map})
    f = open(f"./{OUT_DIR}/{NN_NAME}_init.json", "wb")
    f.write(bytes(params, 'utf-8'))
    f.close()

    for epoch in range(EPOCHS):
        print(f"epoch {epoch + 1}")
        acc_loss = 0
        for path in files:
            print(f"reading {path}")
            open_file(batch_loader, ctypes.create_string_buffer(path))
            loop_loss, _ = train_loop(model, optimizer, batch_loader)
            acc_loss += loop_loss
            close_file(batch_loader)
        param_map = {}

        for variables in model.trainable_variables:
            param_map[variables.name] = variables.numpy().flatten().tolist()
        params = json.dumps({"parameters": param_map})
        f = open(f"./{OUT_DIR}/{NN_NAME}_e{epoch + 1}.json", "wb")
        f.write(bytes(params, 'utf-8'))
        f.close()
        print("saved model")
        print(f"epoch loss: {acc_loss}")

    print(errors)


if __name__ == '__main__':
    main()

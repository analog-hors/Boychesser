#!/usr/bin/python3

import json, sys

WEIGHT_SCALE = 64
ACTIVATION_RANGE = 127

def save_tensor(file, tensor, scale):
    if type(tensor[0]) != list and len(tensor) == 1:
        file.write(f"{round(tensor[0] * scale)}")
        return
    file.write("[")
    for i in range(len(tensor)):
        if type(tensor[i]) != list:
            file.write(f"{round(tensor[i] * scale)},")
        else:
            save_tensor(file, tensor[i], scale)
            file.write(",")
    file.write("]")

with open(sys.argv[1]) as f:
    state = json.load(f)

with open(sys.argv[2], "w") as file:
    file.write("Nnue {")
    file.write("input_layer:")
    save_tensor(file, state["ft.weight"], ACTIVATION_RANGE)
    file.write(",input_layer_bias:")
    save_tensor(file, state["ft.bias"], ACTIVATION_RANGE)
    file.write(",hidden_layer:")
    save_tensor(file, state["out.weight"], WEIGHT_SCALE)
    file.write(",hidden_layer_bias:")
    save_tensor(file, state['out.bias'], ACTIVATION_RANGE * WEIGHT_SCALE)
    file.write("}")

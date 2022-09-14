ACTIVATION_RANGE = 127
WEIGHT_SCALE = 64

def quantize(tensor, scale: int):
    q = []
    for elem in tensor:
        if type(elem) == list:
            q.append(quantize(elem, scale))
        else:
            q.append(round(elem * scale))
    return q

def transpose(matrix):
    t = [[0] * len(matrix) for _ in range(len(matrix[0]))]
    for i in range(len(matrix[0])):
        for j in range(len(matrix)):
            t[i][j] = matrix[j][i]
    return t

def to_frozenight(params):
    return {
        "ft.weight": transpose(quantize(params["ft.weight"], ACTIVATION_RANGE)),
        "ft.bias": quantize(params["ft.bias"], ACTIVATION_RANGE),
        "out.weight": quantize(params["out.weight"], WEIGHT_SCALE),
        "out.bias": quantize(params["out.bias"], ACTIVATION_RANGE * WEIGHT_SCALE),
    }

import os, sys, json, itertools

def grouper(iterable, n: int):
    iterator = iter(iterable)
    while chunk := list(itertools.islice(iterator, n)):
        yield chunk

def quantize(n):
    n = round(n * 160)
    assert n in range(-32768, 32768)
    return n

os.system(f"tar -xf {sys.argv[1]} 0-10.json")
with open("0-10.json") as weights_file:
    weights = json.load(weights_file)["params.weight"][0]
os.remove("0-10.json")

features = len(weights) // 2
mg_weights = weights[:features]
eg_weights = weights[features:]
quantized = [quantize(eg) * 0x10000 + quantize(mg) for mg, eg in zip(mg_weights, eg_weights)]
print("int[] constants = {")
for weights in grouper(quantized, 6):
    print("    " + ", ".join(map(str, weights)) + ",")
print("}")

code = """
    {}
    + {} * y 
    + {} * Min(square.File, 7 - square.File)
    + {} * Min(y, 7 - y)
    + {} * BitboardHelper.GetNumberOfSetBits(
        BitboardHelper.GetSliderAttacks(
            (PieceType)Min(5, pieceType + 1), square, board)
        )
    + {} * Abs(square.File - board.GetKingSquare(white).File)
"""
params = (f"constants[{f'{offset} + ' if offset > 0 else ''}pieceType]" for offset in range(0, features, 6))
print(code.format(*params))

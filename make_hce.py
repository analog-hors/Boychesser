import os, sys, json

EVAL_SCALE = 160

os.system(f"tar -xf {sys.argv[1]} 0-15.json")
with open("0-15.json") as weights_file:
    raw_weights: list[float] = json.load(weights_file)["params.weight"][0]
os.remove("0-15.json")

features = len(raw_weights) // 2
mg_weights = raw_weights[:features]
eg_weights = raw_weights[features:]
weights = zip(mg_weights, eg_weights)

def take(n: int) -> list[tuple[float, float]]:
    return [next(weights) for _ in range(n)]

def quantize_i16(n: float) -> int:
    n = round(n * EVAL_SCALE)
    assert n >= -32768 and n <= 32767
    return n

def quantize_i16_error(n: float) -> float:
    return n - quantize_i16(n) / EVAL_SCALE

def quantize_u8(n: float) -> int:
    n = round(n * EVAL_SCALE)
    assert n >= 0 and n <= 255
    return n

def packed_eval(mg: float, eg: float) -> int:
    return quantize_i16(eg) * 0x10000 + quantize_i16(mg)

def encode_weights(weights: list[tuple[float, float]]) -> list[int]:
    return [packed_eval(mg, eg) for mg, eg in weights]

def print_weight(name: str, weight: tuple[float, float]):
    value = packed_eval(weight[0], weight[1])
    print(f"{name} value: {value:#010x}")

def encode_psts_and_material(psts: list[list[tuple[float, float]]], material: list[tuple[float, float]]) -> tuple[list[int], list[int]]:
    assert len(psts) == len(material)
    material_consts = []
    pst_deltas = []
    for pst, (mg_mat, eg_mat) in zip(psts, material):
        assert len(pst) == 32
        material_consts.append(packed_eval(mg_mat, eg_mat))
        mg_error = quantize_i16_error(mg_mat)
        eg_error = quantize_i16_error(eg_mat)
        for i in range(0, 32, 4):
            w = [(quantize_u8(mg + mg_error), quantize_u8(eg + eg_error)) for mg, eg in pst[i:i + 4]]
            (am, ae), (bm, be), (cm, ce), (dm, de) = w
            b = [de, ce, dm, cm, be, ae, bm, am]
            pst_deltas.append(int.from_bytes(b, "big"))
    # shift all values to the right (removing the redundant king value)
    # this layout saves tokens in the eval code
    material_consts.insert(0, 0)
    material_consts.pop()
    return pst_deltas, material_consts

pawn_pst = take(32)
knight_pst = take(32)
bishop_pst = take(32)
rook_pst = take(32)
queen_pst = take(32)
king_pst = take(32)
material = take(6)
mobility = take(4)
tempo = take(1)
own_pawns_file = take(6)
for _ in weights: assert False

print_weight("tempo", *tempo)
all_psts = [pawn_pst, knight_pst, bishop_pst, rook_pst, queen_pst, king_pst]
pst_deltas, material_consts = encode_psts_and_material(all_psts, material)
mobility_consts = encode_weights(mobility)
own_pawns_file_consts = encode_weights(own_pawns_file)

packed_data = []

def add_i32_data(all_consts: list[tuple[str, list[int]]]):
    flattened = []
    for name, consts in all_consts:
        offset = len(packed_data) * 2 + len(flattened)
        print(f"{name} offset: {offset} (i32 data)")
        flattened.extend(consts)
    for i in range(0, len(flattened), 2):
        lo = flattened[i]
        hi = flattened[i + 1] if i + 1 < len(flattened) else 0
        packed_data.append((hi % 2 ** 32) << 32 | (lo % 2 ** 32))

def add_u64_data(name: str, consts: list[int]):
    print(f"{name} offset: {len(packed_data)} (u64 data)")
    packed_data.extend(consts)

add_i32_data([
    ("material", material_consts),
    ("mobility", mobility_consts),
    ("own_pawns_file", own_pawns_file_consts),
])
add_u64_data("psts", pst_deltas)

print("ulong[] packedData = {", end="")
for i, n in enumerate(packed_data):
    if i % 4 == 0:
        print()
        print("   ", end="")
    print(f" {n:#018x},", end="")
print()
print("};")

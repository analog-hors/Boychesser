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
    # remove the redundant king value
    material_consts.pop()
    return pst_deltas, material_consts

opposite_pawn_pst = take(32)
pawn_pst = take(32)
knight_pst = take(32)
bishop_pst = take(32)
rook_pst = take(32)
queen_pst = take(32)
king_pst = take(32)
material = take(7)
mobility = take(4)
tempo = take(1)
own_pawns_file = take(7)
for _ in weights: assert False

print_weight("tempo", *tempo)
all_psts = [opposite_pawn_pst, pawn_pst, knight_pst, bishop_pst, rook_pst, queen_pst, king_pst]
pst_deltas, material_consts = encode_psts_and_material(all_psts, material)
mobility_consts = encode_weights(mobility)
own_pawns_file_consts = encode_weights(own_pawns_file)

# interpet as LE u64 array
packed_data_bytes: list[int | None] = [None] * 520

def add_u8_data(offset: int, data: list[int]):
    assert all(n <= 255 for n in data)
    for i, n in enumerate(data):
        if packed_data_bytes[offset + i] is not None:
            raise Exception("attempted to write to non-empty data slot")
        packed_data_bytes[offset + i] = n

def add_i32_data(offset: int, data: list[int]):
    assert all(n >= -2147483648 and n <= 2147483647 for n in data)
    data_bytes = []
    for n in data:
        data_bytes.extend(n.to_bytes(4, "little", signed=True))
    add_u8_data(offset * 4, data_bytes)

def add_u64_data(offset: int, data: list[int]):
    assert all(n < 2 ** 64 for n in data)
    data_bytes = []
    for n in data:
        data_bytes.extend(n.to_bytes(8, "little", signed=False))
    add_u8_data(offset * 8, data_bytes)

add_u64_data(0, pst_deltas)
add_i32_data(112, material_consts)
add_i32_data(118, mobility_consts)
add_i32_data(122, own_pawns_file_consts)

packed_data = []
for i in range(0, len(packed_data_bytes), 8):
    u64_bytes = [n or 0 for n in packed_data_bytes[i: i + 8]]
    packed_data.append(int.from_bytes(u64_bytes, "little", signed=False))
print("ulong[] packedData = {", end="")
for i, n in enumerate(packed_data):
    if i % 4 == 0:
        print()
        print("   ", end="")
    print(f" {n:#018x},", end="")
print()
print("};")

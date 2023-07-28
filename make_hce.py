import os, sys, json

EVAL_SCALE = 160

os.system(f"tar -xf {sys.argv[1]} 0-10.json")
with open("0-10.json") as weights_file:
    raw_weights: list[float] = json.load(weights_file)["params.weight"][0]
os.remove("0-10.json")

features = len(raw_weights) // 2
mg_weights = raw_weights[:features]
eg_weights = raw_weights[features:]
weights = zip(mg_weights, eg_weights)
pst_deltas: list[int] = []
eval_consts: list[int] = []

def take(n: int) -> list[tuple[float, float]]:
    return [next(weights) for _ in range(n)]

def pos() -> int:
    return len(pst_deltas) * 2 + len(eval_consts)

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

def add_weights(name: str, weights: list[tuple[float, float]]):
    print(f"{name} offset: {pos()}")
    for mg, eg in weights:
        eval_consts.append(packed_eval(mg, eg))

def print_weight(name: str, weight: tuple[float, float]):
    value = packed_eval(weight[0], weight[1])
    print(f"{name} value: {value:#010x}")

def add_psts(psts: list[list[tuple[float, float]]], material_weights: list[tuple[float, float]]):
    print("pst offset: 0")
    print(f"material offset: {len(psts) * 8 * 2}")
    assert len(pst_deltas) == 0
    assert len(eval_consts) == 0
    assert len(psts) == len(material_weights)
    for pst, (mg_mat, eg_mat) in zip(psts, material_weights):
        assert len(pst) == 32
        eval_consts.append(packed_eval(mg_mat, eg_mat))
        mg_error = quantize_i16_error(mg_mat)
        eg_error = quantize_i16_error(eg_mat)
        for i in range(0, 32, 4):
            w = [(quantize_u8(mg + mg_error), quantize_u8(eg + eg_error)) for mg, eg in pst[i:i + 4]]
            (am, ae), (bm, be), (cm, ce), (dm, de) = w
            b = [de, ce, dm, cm, be, ae, bm, am]
            pst_deltas.append(int.from_bytes(b, "big"))

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

add_psts([pawn_pst, knight_pst, bishop_pst, rook_pst, queen_pst, king_pst], material)
add_weights("mobility", mobility)
add_weights("own_pawns_file", own_pawns_file)
print_weight("tempo", *tempo)

packed_eval_consts = [*pst_deltas]
for i in range(0, len(eval_consts), 2):
    lo = eval_consts[i]
    hi = eval_consts[i + 1] if i + 1 < len(eval_consts) else 0
    packed_eval_consts.append((hi % 2 ** 32) << 32 | (lo % 2 ** 32))
print("ulong[] packedEvalConsts = {", end="")
for i, n in enumerate(packed_eval_consts):
    if i % 4 == 0:
        print()
        print("   ", end="")
    print(f" {n:#018x},", end="")
print()
print("};")

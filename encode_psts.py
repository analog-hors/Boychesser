import sys, json

tables = json.load(sys.stdin)

def idx(table, i):
    return table[i ^ 0b111_000]

def normalize(table, value):
    lo = min(table)
    hi = max(table)
    mid = (lo + hi) // 2
    base = value + mid
    table = [min(max(n - mid, -128), 127) for n in table]
    return base, table

out_bases = []
out_tables = []
for piece_index, piece in enumerate(["pawn", "knight", "bishop", "rook", "queen", "king"]):
    mg_base, mg_table = normalize(tables[f"mg_{piece}_table"], tables["mg_value"][piece_index])
    eg_base, eg_table = normalize(tables[f"eg_{piece}_table"], tables["eg_value"][piece_index])
    base = ((eg_base << 16) + mg_base) % (2 ** 32)
    table = [0] * (64 // 4)
    for sq in range(64):
        i, s = divmod(sq, 4)
        mg = idx(mg_table, sq)
        eg = idx(eg_table, sq)
        packed = ((mg % 256) << 8) | (eg % 256)
        table[i] |= packed << (s * 16)
    out_bases.append(base)
    out_tables.extend(table)

print("ulong[] PST_BASES = {", end="")
print(*map(hex, out_bases), sep=", ", end="")
print("}, PST_TABLES = {", end="")
print(*map(hex, out_tables), sep=", ", end="")
print("};")

import sys
n = 0
while data := sys.stdin.buffer.read(32):
    data = list(data)
    ev = int.from_bytes(data[28:30], byteorder="little", signed=True)
    eval = round(ev / 115 * 1016)
    n += 1
    if (n % 100000) == 0:
        print(ev, "->", eval, ":", n, file=sys.stderr)
    data[28:30] = int.to_bytes(eval, length=2, byteorder="little", signed=True)
    sys.stdout.buffer.write(bytes(data))

#!/bin/bash
set -e

TRAIN_ID="$(date -u '+%F-%H%M%S')-$1"

NETS=`mktemp -d`

python3 trainer/main.py --nndir "$NETS" --data "$1.bin" --scale 1016 --save-epochs 1 \
    --lr 0.001 --epochs 45 --lr-drop 30 --wdl 0.1 \
    | tee /dev/stderr >"$NETS"/log

pushd "$NETS" >/dev/null
tar cf networks.tar *
popd >/dev/null
zstd "$NETS/networks.tar" -o "$TRAIN_ID.tar.zst"
echo "$TRAIN_ID"
rm -rf "$NETS"

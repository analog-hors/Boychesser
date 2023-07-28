#!/bin/bash
set -e

RUSTFLAGS='-C target-cpu=native' cargo build --release -p parse
rm libparse.so
mv target/release/libparse.so ./

TRAIN_ID="$(date -u '+%F-%H%M%S')-$1"

NETS=`mktemp -d`

# d7-50M-v3, 1016 scale, 0.5 wdl, 10 epochs, lrdrop 9, 0.001 lr
python3.8 trainer/main.py --nndir "$NETS" --data "datasets/$1" --scale 1016 --save-epochs 1 \
    --lr 0.001 --epochs 15 --lr-drop 13 --wdl 0.5 \
    | tee /dev/stderr >"$NETS"/log

pushd "$NETS" >/dev/null
tar cf networks.tar *
popd >/dev/null
zstd "$NETS/networks.tar" -o "$TRAIN_ID.tar.zst"
echo "$TRAIN_ID"
rm -rf "$NETS"

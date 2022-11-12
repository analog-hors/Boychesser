#!/bin/bash
set -e

TRAIN_ID="$(date -u '+%F-%H%M%S')-$1"

[ -e nn ] && rm -r nn; mkdir nn
zstd -d "/content/drive/MyDrive/datasets/$1.bin.zst" -o data.bin

python3 trainer/main.py --data data.bin --scale 1016 --save-epochs 1 \
    --lr 0.001 --epochs 45 --lr-drop 30 --wdl 0.1 \
    | tee nn/log

tar cf networks.tar -C nn .
zstd networks.tar -o "/content/drive/MyDrive/networks/$TRAIN_ID.tar.zst"

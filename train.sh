#!/bin/bash
set -e

TRAIN_ID="$(date -u '+%F-%H%M%S')-$1"

[ -e nn ] && rm -r nn; mkdir nn
[ -e data ] && rm -r data; mkdir data
zstd -d "/content/drive/MyDrive/datasets/$1.bin.zst" -o data/data.bin

python3 trainer/main.py --data-root data --scale 1016 --save-epochs 1 \
    --lr 0.001 --epochs 75 --lr-drop 50 --wdl 0.1 \
    | tee nn/log

tar cf networks.tar -C nn .
zstd networks.tar -o "/content/drive/MyDrive/networks/$TRAIN_ID.tar.zst"

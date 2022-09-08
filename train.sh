#!/bin/bash

TRAIN_ID="$(date -u '+%F-%N')-$1"

rm -r nn data
mkdir -p nn data
zstd -d "/content/drive/MyDrive/datasets/$1.bin.zst" -o data/data.bin

python3 trainer/main.py --data-root .data --scale 1016 --save-epochs 5 --lr 0.001 --epochs 50

tar cf networks.tar nn
zstd networks.tar -o "/content/drive/MyDrive/networks/$TRAIN_ID.bin.zst"

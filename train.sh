#!/bin/bash

TRAIN_ID=$(date -u '+%F-%N')-`basename "$1" .bin`

cargo build --release --lib
cp target/release/libparse.so .
mkdir -p nn/ runs/ networks/ builds/ .data/
ln $1 .data/

python3 trainer/main.py --train-id "$TRAIN_ID" --data-root .data --scale 1016 --save-epochs 5 --lr 0.001 --epochs 50

rm .data/*

CUTECHESS_ARGS="-repeat -recover -games 5000 -tournament knockout -concurrency 16"
CUTECHESS_ARGS="$CUTECHESS_ARGS -openings file=4moves_noob.epd format=epd order=random"
CUTECHESS_ARGS="$CUTECHESS_ARGS -draw movenumber=40 movecount=5 score=10"
CUTECHESS_ARGS="$CUTECHESS_ARGS -resign movecount=4 score=500"
CUTECHESS_ARGS="$CUTECHESS_ARGS -each nodes=10000 proto=uci"

for net in nn/"$TRAIN_ID"_*.json; do
    ./json-to-frozenight.py "$net" ../frozenight/frozenight/model.rs
    pushd ../frozenight
    cargo build --release
    popd
    EPOCH="$(echo $net | grep -Eo '[0-9]+\.json' | grep -Eo '[0-9]+')"
    cp ../frozenight/target/release/frozenight-uci builds/$EPOCH
    CUTECHESS_ARGS="$CUTECHESS_ARGS -engine name=$EPOCH cmd=builds/$EPOCH"
done

FIND_WINNER=<<-AWK
{
    if (match($0, /^\t+/) && RLENGTH > best) {
        best = RLENGTH;
        engine = $1;
    }
}
END {
    print engine;
}
AWK

WINNER=$(cutechess-cli $CUTECHESS_ARGS | awk "$FIND_WINNER")
cp nn/"$TRAIN_ID"_$WINNER.json networks/$TRAIN_ID.json
echo $TRAIN_ID

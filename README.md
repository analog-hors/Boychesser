# Boychesser

A very small C# chess engine written for [Sebastian Lague's Tiny Chess Bots Challenge](https://github.com/SebLague/Chess-Challenge). Estimated to be around 2750 Elo on the [CCRL Blitz list](https://www.computerchess.org.uk/ccrl/404/).

## Features

### Board representation
- Based entirely on Sebastian Lague's framework

### Search
- Principal variation search
- Quiescence search (integrated with main search)
- Transposition table
- Aspiration windows
- Pruning
    - Null move pruning
    - Reverse futility pruning
    - Delta pruning
    - Futility pruning
    - Late move pruning
- Reductions
    - Late move reduction
    - History reduction
    - Internal iterative reduction
- Extensions
    - Check extension
- Move ordering
    - TT move
    - MVV-LVA
    - History heuristic (with gravity)

### Evaluation
- Phased evaluation
- Material
- Mirrored piece square tables
- King-relative pawn
- Slider mobility
- Virtual queen mobility
- Friendly pawns in front of a piece
- Tempo
- Tuned on an evenly-mixed combination of Frozenight NNUE data and `lichess-big3-resolved`

### Time management
- Based on a fixed percentage of remaining time
- Aborts after an iteration if a "soft" limit is exceeded
- Aborts during an iteration if a "hard" limit is exceeded

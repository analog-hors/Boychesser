using ChessChallenge.API;
using System;
using static System.Math;
using static ChessChallenge.API.BitboardHelper;

public class MyBot : IChessBot {
    public int maxDepth = 999; // #DEBUG

    public long nodes = 0; // #DEBUG
    public int maxSearchTime, searchingDepth, lastScore;

    Timer timer;
    Board board;

    Move searchBestMove, rootBestMove;

    // this tuple is 24 bytes, so the transposition table is precisely 192MiB (~201 MB)
    readonly (
        ulong, // hash
        ushort, // moveRaw
        int, // score
        int, // depth
        int // bound BOUND_EXACT=[1, 2147483647), BOUND_LOWER=2147483647, BOUND_UPPER=0
    )[] transpositionTable = new (ulong, ushort, int, int, int)[0x800000];

    // piece-to history tables, per-color
    readonly int[,,] history = new int[2, 7, 64];

    int[] mg_value = new int[] { 82, 337, 365, 477, 1025,  0};
    int[] eg_value = new int[] { 94, 281, 297, 512,  936,  0};

    int[] mg_pawn_table = new int[] {
        0,   0,   0,   0,   0,   0,  0,   0,
        98, 134,  61,  95,  68, 126, 34, -11,
        -6,   7,  26,  31,  65,  56, 25, -20,
        -14,  13,   6,  21,  23,  12, 17, -23,
        -27,  -2,  -5,  12,  17,   6, 10, -25,
        -26,  -4,  -4, -10,   3,   3, 33, -12,
        -35,  -1, -20, -23, -15,  24, 38, -22,
        0,   0,   0,   0,   0,   0,  0,   0,
    };

    int[] eg_pawn_table = new int[] {
        0,   0,   0,   0,   0,   0,   0,   0,
        178, 173, 158, 134, 147, 132, 165, 187,
        94, 100,  85,  67,  56,  53,  82,  84,
        32,  24,  13,   5,  -2,   4,  17,  17,
        13,   9,  -3,  -7,  -7,  -8,   3,  -1,
        4,   7,  -6,   1,   0,  -5,  -1,  -8,
        13,   8,   8,  10,  13,   0,   2,  -7,
        0,   0,   0,   0,   0,   0,   0,   0,
    };

    int[] mg_knight_table = new int[] {
        -167, -89, -34, -49,  61, -97, -15, -107,
        -73, -41,  72,  36,  23,  62,   7,  -17,
        -47,  60,  37,  65,  84, 129,  73,   44,
        -9,  17,  19,  53,  37,  69,  18,   22,
        -13,   4,  16,  13,  28,  19,  21,   -8,
        -23,  -9,  12,  10,  19,  17,  25,  -16,
        -29, -53, -12,  -3,  -1,  18, -14,  -19,
        -105, -21, -58, -33, -17, -28, -19,  -23,
    };

    int[] eg_knight_table = new int[] {
        -58, -38, -13, -28, -31, -27, -63, -99,
        -25,  -8, -25,  -2,  -9, -25, -24, -52,
        -24, -20,  10,   9,  -1,  -9, -19, -41,
        -17,   3,  22,  22,  22,  11,   8, -18,
        -18,  -6,  16,  25,  16,  17,   4, -18,
        -23,  -3,  -1,  15,  10,  -3, -20, -22,
        -42, -20, -10,  -5,  -2, -20, -23, -44,
        -29, -51, -23, -15, -22, -18, -50, -64,
    };

    int[] mg_bishop_table = new int[] {
        -29,   4, -82, -37, -25, -42,   7,  -8,
        -26,  16, -18, -13,  30,  59,  18, -47,
        -16,  37,  43,  40,  35,  50,  37,  -2,
        -4,   5,  19,  50,  37,  37,   7,  -2,
        -6,  13,  13,  26,  34,  12,  10,   4,
        0,  15,  15,  15,  14,  27,  18,  10,
        4,  15,  16,   0,   7,  21,  33,   1,
        -33,  -3, -14, -21, -13, -12, -39, -21,
    };

    int[] eg_bishop_table = new int[] {
        -14, -21, -11,  -8, -7,  -9, -17, -24,
        -8,  -4,   7, -12, -3, -13,  -4, -14,
        2,  -8,   0,  -1, -2,   6,   0,   4,
        -3,   9,  12,   9, 14,  10,   3,   2,
        -6,   3,  13,  19,  7,  10,  -3,  -9,
        -12,  -3,   8,  10, 13,   3,  -7, -15,
        -14, -18,  -7,  -1,  4,  -9, -15, -27,
        -23,  -9, -23,  -5, -9, -16,  -5, -17,
    };

    int[] mg_rook_table = new int[] {
        32,  42,  32,  51, 63,  9,  31,  43,
        27,  32,  58,  62, 80, 67,  26,  44,
        -5,  19,  26,  36, 17, 45,  61,  16,
        -24, -11,   7,  26, 24, 35,  -8, -20,
        -36, -26, -12,  -1,  9, -7,   6, -23,
        -45, -25, -16, -17,  3,  0,  -5, -33,
        -44, -16, -20,  -9, -1, 11,  -6, -71,
        -19, -13,   1,  17, 16,  7, -37, -26,
    };

    int[] eg_rook_table = new int[] {
        13, 10, 18, 15, 12,  12,   8,   5,
        11, 13, 13, 11, -3,   3,   8,   3,
        7,  7,  7,  5,  4,  -3,  -5,  -3,
        4,  3, 13,  1,  2,   1,  -1,   2,
        3,  5,  8,  4, -5,  -6,  -8, -11,
        -4,  0, -5, -1, -7, -12,  -8, -16,
        -6, -6,  0,  2, -9,  -9, -11,  -3,
        -9,  2,  3, -1, -5, -13,   4, -20,
    };

    int[] mg_queen_table = new int[] {
        -28,   0,  29,  12,  59,  44,  43,  45,
        -24, -39,  -5,   1, -16,  57,  28,  54,
        -13, -17,   7,   8,  29,  56,  47,  57,
        -27, -27, -16, -16,  -1,  17,  -2,   1,
        -9, -26,  -9, -10,  -2,  -4,   3,  -3,
        -14,   2, -11,  -2,  -5,   2,  14,   5,
        -35,  -8,  11,   2,   8,  15,  -3,   1,
        -1, -18,  -9,  10, -15, -25, -31, -50,
    };

    int[] eg_queen_table = new int[] {
        -9,  22,  22,  27,  27,  19,  10,  20,
        -17,  20,  32,  41,  58,  25,  30,   0,
        -20,   6,   9,  49,  47,  35,  19,   9,
        3,  22,  24,  45,  57,  40,  57,  36,
        -18,  28,  19,  47,  31,  34,  39,  23,
        -16, -27,  15,   6,   9,  17,  10,   5,
        -22, -23, -30, -16, -16, -23, -36, -32,
        -33, -28, -22, -43,  -5, -32, -20, -41,
    };

    int[] mg_king_table = new int[] {
        -65,  23,  16, -15, -56, -34,   2,  13,
        29,  -1, -20,  -7,  -8,  -4, -38, -29,
        -9,  24,   2, -16, -20,   6,  22, -22,
        -17, -20, -12, -27, -30, -25, -14, -36,
        -49,  -1, -27, -39, -46, -44, -33, -51,
        -14, -14, -22, -46, -44, -30, -15, -27,
        1,   7,  -8, -64, -43, -16,   9,   8,
        -15,  36,  12, -54,   8, -28,  24,  14,
    };

    int[] eg_king_table = new int[] {
        -74, -35, -18, -18, -11,  15,   4, -17,
        -12,  17,  14,  17,  17,  38,  23,  11,
        10,  17,  23,  15,  20,  45,  44,  13,
        -8,  22,  24,  27,  26,  33,  26,   3,
        -18,  -4,  21,  24,  27,  23,   9, -11,
        -19,  -3,  11,  21,  23,  16,   7,  -9,
        -27, -11,   4,  13,  14,   4,  -5, -17,
        -53, -34, -21, -11, -28, -14, -24, -43
    };

    public Move Think(Board boardOrig, Timer timerOrig) {
        board = boardOrig;
        timer = timerOrig;

        maxSearchTime = timer.MillisecondsRemaining / 4;
        searchingDepth = 1;
        do
            try {
                // Aspiration windows
                if (Abs(lastScore - Negamax(lastScore - 20, lastScore + 20, searchingDepth)) >= 20)
                    Negamax(-32000, 32000, searchingDepth);
                rootBestMove = searchBestMove;
            } catch {
                // out of time
                break;
            }
        while (
            ++searchingDepth <= 200
                && searchingDepth <= maxDepth // #DEBUG
                && timer.MillisecondsElapsedThisTurn < maxSearchTime / 10
        );

        return rootBestMove;
    }

    public int Negamax(int alpha, int beta, int depth) {
        // abort search if out of time, but we must search at least depth 1
        if (timer.MillisecondsElapsedThisTurn >= maxSearchTime && searchingDepth > 1)
            throw null;

        nodes++; // #DEBUG

        ref var tt = ref transpositionTable[board.ZobristKey & 0x7FFFFF];
        var (ttHash, ttMoveRaw, score, ttDepth, ttBound) = tt;

        bool
            ttHit = ttHash == board.ZobristKey,
            nonPv = alpha + 1 == beta,
            inQSearch = depth <= 0,
            pieceIsWhite;
        int
            eval = 0x000b000a, // tempo
            bestScore = board.PlyCount - 30000,
            oldAlpha = alpha,

            // search loop vars
            moveCount = 0, // quietsToCheckTable = [0, 4, 5, 10, 23]
            quietsToCheck = 0b_010111_001010_000101_000100_000000 >> depth * 6 & 0b111111,

            // temp vars
            tmp = 0;
        if (ttHit) {
            if (ttDepth >= depth && ttBound switch {
                2147483647 /* BOUND_LOWER */ => score >= beta,
                0 /* BOUND_UPPER */ => score <= alpha,
                // exact cutoffs at pv nodes causes problems, but need it in qsearch for matefinding
                _ /* BOUND_EXACT */ => nonPv || inQSearch,
            })
                return score;
        } else if (depth > 3)
            // Internal iterative reduction
            depth--;

        // this is a local function because the C# JIT doesn't optimize very large functions well
        // we do packed phased evaluation, so weights are of the form (eg << 16) + mg
        int Eval(ulong pieces) {
            int mg = 0, eg = 0;
            // use tmp as phase (initialized above)
            while (pieces != 0) {
                int pieceType, sqIndex;
                Piece piece = board.GetPiece(new(sqIndex = ClearAndGetIndexOfLSB(ref pieces)));
                pieceType = (int)piece.PieceType;

                if (piece.IsWhite) {
                    sqIndex ^= 0b111000;
                }

                int mul = piece.IsWhite == board.IsWhiteToMove ? 1 : -1;

                mg += mg_value[pieceType - 1] * mul;
                eg += eg_value[pieceType - 1] * mul;

                mg += piece.PieceType switch {
                    PieceType.Pawn => mg_pawn_table[sqIndex],
                    PieceType.Knight => mg_knight_table[sqIndex],
                    PieceType.Bishop => mg_bishop_table[sqIndex],
                    PieceType.Rook => mg_rook_table[sqIndex],
                    PieceType.Queen => mg_queen_table[sqIndex],
                    PieceType.King => mg_king_table[sqIndex],
                } * mul;
                eg += piece.PieceType switch {
                    PieceType.Pawn => eg_pawn_table[sqIndex],
                    PieceType.Knight => eg_knight_table[sqIndex],
                    PieceType.Bishop => eg_bishop_table[sqIndex],
                    PieceType.Rook => eg_rook_table[sqIndex],
                    PieceType.Queen => eg_queen_table[sqIndex],
                    PieceType.King => eg_king_table[sqIndex],
                } * mul;

                // phaseWeightTable = [0, 0, 1, 1, 2, 4, 0]
                tmp += 0x0421100 >> pieceType * 4 & 0xF;
            }
            // the correct way to extract EG eval is (eval + 0x8000) >> 16, but this is shorter and
            // the off-by-one error is insignificant
            // the division is also moved outside Eval to save a token

            return mg * tmp + eg * (24 - tmp);
            // end tmp use
        }
        // using tteval in qsearch causes matefinding issues
        eval = ttHit && !inQSearch ? score : Eval(board.AllPiecesBitboard) / 24;

        if (inQSearch)
            // stand pat in quiescence search
            alpha = Max(alpha, bestScore = eval);
        else if (nonPv && eval >= beta && board.TrySkipTurn()) {
            // Pruning based on null move observation
            bestScore = depth <= 4
                // Reverse Futility Pruning
                ? eval - 58 * depth
                // Adaptive Null Move Pruning
                : -Negamax(-beta, -alpha, (depth * 100 + beta - eval) / 186 - 1);
            board.UndoSkipTurn();
        }
        if (bestScore >= beta)
            return bestScore;

        if (board.IsInStalemate())
            return 0;

        var moves = board.GetLegalMoves(inQSearch);
        var scores = new int[moves.Length];
        // use tmp as scoreIndex
        tmp = 0;
        foreach (Move move in moves)
            // move ordering:
            // 1. hashmove
            // 2. captures (ordered by most valuable victim, least valuable attacker)
            // 3. quiets (ordered by history)
            scores[tmp++] -= ttHit && move.RawValue == ttMoveRaw ? 1000000
                : Max(
                    (int)move.CapturePieceType * 32768 - (int)move.MovePieceType - 16384,
                    HistoryValue(move)
                );
        // end tmp use

        Array.Sort(scores, moves);
        Move bestMove = default;
        foreach (Move move in moves) {
            // Delta pruning
            // deltas = [180, 390, 442, 718, 1332]
            // due to sharing of the top bit of each entry with the bottom bit of the next one
            // (expands the range of values for the queen) all deltas must be even (except pawn)
            if (inQSearch && eval + (0b1_0100110100_1011001110_0110111010_0110000110_0010110100_0000000000 >> (int)move.CapturePieceType * 10 & 0b1_11111_11111) <= alpha)
                break;

            board.MakeMove(move);
            int
                // Check extension
                nextDepth = board.IsInCheck() ? depth : depth - 1,
                reduction = (depth - nextDepth) * Max(
                    // Late move reduction
                    (moveCount * 93 + depth * 144) / 1000
                        // History reduction
                        + scores[moveCount] / 172,
                    0
                );
            if (board.IsRepeatedPosition())
                score = 0;
            else {
                // this crazy while loop does the null window searches for PVS: first it searches
                // with the reduced depth, and if it beats alpha it re-searches at full depth
                // ~alpha is equivalent to -alpha-1 under two's complement
                while (
                    moveCount != 0
                        && (score = -Negamax(~alpha, -alpha, nextDepth - reduction)) > alpha
                        && reduction != 0
                )
                    reduction = 0;
                if (moveCount == 0 || score > alpha)
                    score = -Negamax(-beta, -alpha, nextDepth);
            }

            board.UndoMove(move);

            if (score > bestScore) {
                alpha = Max(alpha, bestScore = score);
                bestMove = move;
            }
            if (score >= beta) {
                if (!move.IsCapture) {
                    // use tmp as change
                    // Increased history change when eval < alpha
                    // equivalent to tmp = eval < alpha ? -(depth + 1) : depth
                    // 1. eval - alpha is < 0 if eval < alpha and >= 0 otherwise
                    // 2. >> 31 maps numbers < 0 to -1 and numbers >= 0 to 0
                    // 3. -1 ^ depth = ~depth while 0 ^ depth = depth
                    // 4. ~depth = -depth - 1 = -(depth + 1)
                    // since we're squaring tmp, sign doesn't matter
                    tmp = eval - alpha >> 31 ^ depth;
                    tmp *= tmp;
                    foreach (Move malusMove in moves.AsSpan(0, moveCount))
                        if (!malusMove.IsCapture)
                            HistoryValue(malusMove) -= tmp + tmp * HistoryValue(malusMove) / 512;
                    HistoryValue(move) += tmp - tmp * HistoryValue(move) / 512;
                    // end tmp use
                }
                break;
            }

            // pruning techniques that break the move loop
            if (nonPv && depth <= 4 && !move.IsCapture && (
                // Late move pruning
                quietsToCheck-- == 1 ||
                // Futility pruning
                eval + 127 * depth < alpha
            ))
                break;

            moveCount++;
        }

        tt = (
            board.ZobristKey,
            alpha > oldAlpha // don't update best move if upper bound
                ? bestMove.RawValue
                : ttMoveRaw,
            Clamp(bestScore, -20000, 20000),
            Max(depth, 0),
            bestScore >= beta
                ? 2147483647 /* BOUND_LOWER */
                : alpha - oldAlpha /* BOUND_UPPER if alpha == oldAlpha else BOUND_EXACT */
        );

        searchBestMove = bestMove;
        return lastScore = bestScore;
    }

    ref int HistoryValue(Move move) => ref history[
        board.PlyCount & 1,
        (int)move.MovePieceType,
        move.TargetSquare.Index
    ];
}

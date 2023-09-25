using ChessChallenge.API;
using System;
using static System.Math;
using static ChessChallenge.API.BitboardHelper;

public class MyBot : IChessBot {
    public int maxDepth = 999; // #DEBUG

    public long nodes = 0; // #DEBUG
    public int maxSearchTime, searchingDepth, lastScore;

    public Timer timer;
    public Board board;

    Move nullMove, searchBestMove, rootBestMove;

    // Assuming the size of TtEntry is indeed 24 bytes, this table is precisely 192MiB (~201.327 MB).
    (
        ulong, // hash
        ushort, // moveRaw
        int, // score
        int, // depth 
        int // bound BOUND_EXACT=[1, 2147483647), BOUND_LOWER=2147483647, BOUND_UPPER=0
    )[] transpositionTable = new (ulong, ushort, int, int, int)[0x800000];

    int[,,] history = new int[2, 7, 64];

    ulong[] packedData = {
        0x0000000000000000, 0x1d23130e29301208, 0x1f1f1b0c1d181606, 0x0621342231341a0b,
        0x2333301f3134200e, 0x5d5d3b43705a2f1d, 0x939f6063a7a15f4d, 0x0007000300090008,
        0xfffffffb000a0002, 0x351c00241f1b2d06, 0x13250e131e142517, 0x2226181526231404,
        0x1f23261c32341604, 0x595f2c3f896a3217, 0x7fb96c47ccbc0d1b, 0x0000000000000000,
        0x68544b41453e2f2c, 0x6c664f5761524436, 0x7d716267795d5e2d, 0x878a6d717d716c4b,
        0x969a84777f6f675f, 0x7681ad91736f7e68, 0x726a80796d58604d, 0x70735a1174110d03,
        0x36422f273a40342e, 0x3f3e2e2c33323a36, 0x3937282b3c3e3430, 0x3b4432304c3e3540,
        0x30364b3f434c2e36, 0x3836474336494a33, 0x4d4a303b3944322c, 0x52590004593f0808,
        0x7f94696190985853, 0x838e625485806146, 0x8f8b58578b9c464a, 0x879c544da991424e,
        0x93a6716998a25261, 0x9b987a859ba47862, 0xa9a48a7fa2a4797a, 0x9aa0757daba0857c,
        0xbab68668b0d0716c, 0xafa48583adbd787c, 0xd2ce6a70c5ba747d, 0xead85d75d5d1777c,
        0xfae46c79f9e17488, 0xf9f87b7debe3938c, 0xfbf57c77efef5f82, 0xdbe3a492d7f17b74,
        0x21281b211d013c31, 0x4e3b0419391b2b26, 0x515509044d2e2007, 0x736009034a2e0b00,
        0x726d0c0953412303, 0x74722e3c63484c0a, 0x415c4f404e2d3621, 0x2f282e5724074f15,
        0x0055004200500028, 0x00da00cc00b500a8, 0x02e30268018200e3, 0xffd9ffedffecfff9,
        0xfff80006000d0009, 0xffe70008fff4fff8, 0x00000000fff70012,
    };

    int EvalWeight(int item) => (int)(packedData[item >> 1] >> item * 32);

    public Move Think(Board boardOrig, Timer timerOrig) {
        maxSearchTime = timerOrig.MillisecondsRemaining / 4;

        board = boardOrig;
        timer = timerOrig;
        searchingDepth = 1;

        do
            try {
                // Aspiration windows
                if (Abs(lastScore - Negamax(lastScore - 20, lastScore + 20, searchingDepth, false)) >= 20)
                    Negamax(-32000, 32000, searchingDepth, false);
                rootBestMove = searchBestMove;
            } catch {
                // out of time
                break;
            }
        while (
            ++searchingDepth <= 200
                && searchingDepth <= maxDepth // #DEBUG
                && timerOrig.MillisecondsElapsedThisTurn < maxSearchTime / 10
        );

        return rootBestMove;
    }

    public int Negamax(int alpha, int beta, int depth, bool notRoot = true) {
        // abort search
        if (timer.MillisecondsElapsedThisTurn >= maxSearchTime && searchingDepth > 1)
            throw null;

        nodes++; // #DEBUG

        // check for game end
        if (board.IsInCheckmate())
            return board.PlyCount - 30000;
        if (notRoot && board.IsDraw())
            return 0;

        ref var tt = ref transpositionTable[board.ZobristKey & 0x7FFFFF];
        var (ttHash, ttMoveRaw, score, ttDepth, ttBound) = tt;

        bool
            ttHit = ttHash == board.ZobristKey,
            nonPv = alpha + 1 == beta,
            inQSearch = depth <= 0,
            pieceIsWhite;
        int
            eval = 0x000f0008, // tempo
            bestScore = -99999,
            oldAlpha = alpha,

            // search loop vars
            moveCount = 0, // quietsToCheckTable = [0, 4, 6, 13, 47]
            quietsToCheck = 0b_101111_001101_000110_000100_000000 >> depth * 6 & 0b111111,

            // temp vars
            tmp = 0;
        if (ttHit) {
            if (ttDepth >= depth && ttBound switch {
                2147483647 /* BOUND_LOWER */ => score >= beta,
                0 /* BOUND_UPPER */ => score <= alpha,
                _ /* BOUND_EXACT */ => nonPv || inQSearch,
            })
                return score;
        } else if (depth > 5)
            // Internal Iterative Reduction (IIR) (4 elo (LTC), 10 tokens, 0.4 elo/token)
            depth--;

        int Eval(ulong pieces) {
            // use tmp as phase (initialized above)
            while (pieces != 0) {
                int pieceType, sqIndex;
                Piece piece = board.GetPiece(new(sqIndex = ClearAndGetIndexOfLSB(ref pieces)));
                pieceType = (int)piece.PieceType;
                // virtual pawn type
                // consider pawns on the opposite half of the king as distinct piece types (piece 0)
                // king-relative pawns (vs full pawn pst) (7 elo, 8 tokens, 0.9 elo/token)
                pieceType -= (sqIndex & 0b111 ^ board.GetKingSquare(pieceIsWhite = piece.IsWhite).File) >> 1 >> pieceType;
                sqIndex =
                    // material
                    EvalWeight(112 + pieceType)
                        // psts
                        + (int)(
                            packedData[pieceType * 64 + sqIndex >> 3 ^ (pieceIsWhite ? 0 : 0b111)]
                                >> (0x01455410 >> sqIndex * 4) * 8
                                & 0xFF00FF
                        )
                        // mobility (35 elo, 19 tokens, 1.8 elo/token)
                        + EvalWeight(11 + pieceType) * GetNumberOfSetBits(
                            GetSliderAttacks((PieceType)Min(5, pieceType), new(sqIndex), board)
                        )
                        // own pawn ahead (29 elo, 37 tokens, 0.8 elo/token)
                        + EvalWeight(118 + pieceType) * GetNumberOfSetBits(
                            (pieceIsWhite ? 0x0101010101010100UL << sqIndex : 0x0080808080808080UL >> 63 - sqIndex)
                                & board.GetPieceBitboard(PieceType.Pawn, pieceIsWhite)
                        );
                eval += pieceIsWhite == board.IsWhiteToMove ? sqIndex : -sqIndex;
                // phaseWeightTable = [0, 0, 1, 1, 2, 4, 0]
                tmp += 0x0421100 >> pieceType * 4 & 0xF;
            }
            // note: the correct way to extract EG eval is (eval + 0x8000) >> 16, but token count
            // the division is also moved outside Eval to save a token
            return (short)eval * tmp + eval / 0x10000 * (24 - tmp);
            // end tmp use
        }
        eval = ttHit && !inQSearch ? score : Eval(board.AllPiecesBitboard) / 24;

        if (inQSearch)
            // stand pat in quiescence search
            alpha = Max(alpha, bestScore = eval);
        else if (nonPv && eval >= beta && board.TrySkipTurn()) {
            // Pruning based on null move observation
            bestScore = depth <= 3
                // RFP (66 elo, 10 tokens, 6.6 elo/token)
                ? eval - 51 * depth
                // Adaptive NMP (82 elo, 29 tokens, 2.8 elo/token)
                : -Negamax(-beta, -alpha, (depth * 101 + beta - eval) / 167 - 1);
            board.UndoSkipTurn();
        }
        if (bestScore >= beta)
            return bestScore;

        var moves = board.GetLegalMoves(inQSearch);
        var scores = new int[moves.Length];
        // use tmp as scoreIndex
        tmp = 0;
        foreach (Move move in moves)
            // move ordering:
            // 1. hashmove
            // 2. captures (ordered by MVV-LVA)
            // 3. quiets (ordered by history)
            scores[tmp++] -= ttHit && move.RawValue == ttMoveRaw ? 1000000
                : Max(
                    (int)move.CapturePieceType * 32768 - (int)move.MovePieceType - 16384,
                    HistoryValue(move)
                );
        // end tmp use

        Array.Sort(scores, moves);
        Move bestMove = nullMove;
        foreach (Move move in moves) {
            // Delta pruning (23 elo, 21 tokens, 1.1 elo/token)
            // deltas = [172, 388, 450, 668, 1310]
            // due to sharing of the top bit of each entry with the bottom bit of the next one
            // (expands the range of values for the queen) all deltas must be even (except pawn)
            if (inQSearch && eval + (0b1_0100011110_1010011100_0111000010_0110000100_0010101100_0000000000 >> (int)move.CapturePieceType * 10 & 0b1_11111_11111) <= alpha)
                break;

            board.MakeMove(move);
            int
                // Check extension (20 elo, 12 tokens, 1.7 elo/token)
                nextDepth = board.IsInCheck() ? depth : depth - 1,
                reduction = (depth - nextDepth) * Max(
                    (moveCount * 91 + depth * 140) / 1000
                        // history reduction (5 elo, 4 tokens, 1.2 elo/token)
                        + scores[moveCount] / 227,
                    0
                );
            while (
                moveCount != 0
                    && (score = -Negamax(~alpha, -alpha, nextDepth - reduction)) > alpha
                    && reduction != 0
            )
                reduction = 0;
            if (moveCount == 0 || score > alpha)
                score = -Negamax(-beta, -alpha, nextDepth);

            board.UndoMove(move);

            if (score > bestScore) {
                alpha = Max(alpha, bestScore = score);
                bestMove = move;
            }
            if (score >= beta) {
                if (!move.IsCapture) {
                    // use tmp as change
                    // increased history change when eval < alpha (6 elo, 7 tokens, 0.9 elo/token)
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

            // Pruning techniques that break the move loop
            if (nonPv && depth <= 4 && !move.IsCapture && (
                // LMP (34 elo, 14 tokens, 2.4 elo/token)
                quietsToCheck-- == 1 ||
                // Futility Pruning (11 elo, 8 tokens, 1.4 elo/token)
                eval + 185 * depth < alpha
            ))
                break;

            moveCount++;
        }

        tt = (
            board.ZobristKey,
            alpha > oldAlpha // don't update best move if upper bound (31 elo, 6 tokens, 5.2 elo/token)
                ? bestMove.RawValue
                : ttMoveRaw,
            bestScore,
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

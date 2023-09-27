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

    // Assuming the size of TtEntry is indeed 24 bytes, this table is precisely 192MiB (~201.327 MB).
    readonly (
        ulong, // hash
        ushort, // moveRaw
        int, // score
        int, // depth 
        int // bound BOUND_EXACT=[1, 2147483647), BOUND_LOWER=2147483647, BOUND_UPPER=0
    )[] transpositionTable = new (ulong, ushort, int, int, int)[0x800000];

    readonly int[,,] history = new int[2, 7, 64];

    readonly ulong[] packedData = {
        0x0000000000000000, 0x232817102e2a1501, 0x1f1f221929211607, 0x18202a1c2d261507,
        0x262e3123383a230f, 0x585b47456e66331d, 0x8e996f66a6a96050, 0x0002000300070005,
        0xfffdfffd00060001, 0x2a1e011d20152406, 0x221b0c171e14230d, 0x1a1a131b271b1507,
        0x232c21243932200c, 0x5a613342826b2812, 0x8db65a45c8c01014, 0x0000000000000000,
        0x615a4240433a3a30, 0x6f6851525f58423e, 0x8177625b6f595745, 0x8a886a677a6a6352,
        0x938c757b826b6563, 0x7d809a9072638361, 0x756f7b806f565e50, 0x6b7858146e130000,
        0x383b24203f342728, 0x3b37222c3a392f30, 0x403e252b3c42312c, 0x373e3326423a272f,
        0x393b3b3444422e2c, 0x353e424142474339, 0x4642272c46402b23, 0x5253000050510008,
        0x888f655d8f925957, 0x8a8b5b5a898f5447, 0x909055528f8f524e, 0x96985b549a974c4e,
        0x9a9c68679e9e615b, 0x989c817b9b9c7c6b, 0xa09f8a8ca59c7175, 0xa2a18486a09c7f86,
        0xbdae7775b9cb736b, 0xbbb27c7cb0be7a77, 0xc9cf7477cbc67878, 0xe4de7071ddd97677,
        0xf4f07275eedc7683, 0xfafa8383dfe3918f, 0xfffe7b82f5ec7180, 0xe0e89a94e1ed846d,
        0x2027252418003d39, 0x4c42091d31193035, 0x5f560001422c180a, 0x6e6200004d320200,
        0x756c000d5f3c0f01, 0x6f6c323e663e3f1d, 0x535b55395c293b1a, 0x2e1e3c5e22005200,
        0x004d0037004b001f, 0x00de00bf00c000ac, 0x02e1026f018a00eb, 0xffdcffedffdcfff2,
        0xfff9000700010007, 0xffe90003ffeefff3, 0x00000000fff5000d,
    };

    int EvalWeight(int item) => (int)(packedData[item >> 1] >> item * 32);

    public Move Think(Board boardOrig, Timer timerOrig) {
        board = boardOrig;
        timer = timerOrig;

        maxSearchTime = timer.MillisecondsRemaining / 4;
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
                && timer.MillisecondsElapsedThisTurn < maxSearchTime / 10
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
            eval = 0x000b000a, // tempo
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

        // this is a local function because the C# JIT doesn't optimize very large functions well
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
                        )
                        + 0x00320015 * (
                            piece.IsKing
                                ? GetNumberOfSetBits(
                                    board.GetPieceBitboard(PieceType.Bishop, pieceIsWhite)
                                ) / 2
                                : 0
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
        Move bestMove = default;
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

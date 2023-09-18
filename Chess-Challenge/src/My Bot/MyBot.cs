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

    // Assuming the size of TtEntry is indeed 16 bytes, this table is precisely 256MiB.
    (
        ulong, // hash
        ushort, // moveRaw
        short, // score
        short, // depth 
        ushort // bound BOUND_EXACT=[1, 65535), BOUND_LOWER=65535, BOUND_UPPER=0
    )[] transpositionTable = new (ulong, ushort, short, short, ushort)[0x1000000];

    int[,,] history = new int[2, 7, 64];

    ulong[] packedData = {
        0x0000000000000000, 0x2328170f2d2a1401, 0x1f1f221929211507, 0x18202a1c2d261507,
        0x252e3022373a230f, 0x585b47456d65321c, 0x8d986f66a5a85f50, 0x0002000300070005,
        0xfffdfffd00060001, 0x2b1f011d20162306, 0x221c0b171f15220d, 0x1b1b131b271c1507,
        0x232d212439321f0b, 0x5b623342826c2812, 0x8db65b45c8c01014, 0x0000000000000000,
        0x615a413e423a382e, 0x6f684f506059413c, 0x82776159705a5543, 0x8b8968657a6a6150,
        0x948c7479826c6361, 0x7e81988f73648160, 0x766f7a7e70585c4e, 0x6c7956116e100000,
        0x3a3d2d2840362f31, 0x3c372a343b3a3838, 0x403e2e343c433934, 0x373e3b2e423b2f37,
        0x383b433c45433634, 0x353d4b4943494b41, 0x46432e354640342b, 0x55560000504f0511,
        0x878f635c8f915856, 0x8a8b5959898e5345, 0x8f9054518f8e514c, 0x96985a539a974a4c,
        0x9a9c67659e9d5f59, 0x989c807a9b9c7a6a, 0xa09f898ba59c6f73, 0xa1a18386a09b7e84,
        0xbcac7774b8c9736a, 0xbab17b7caebd7976, 0xc9ce7376cac57878, 0xe4de6f70dcd87577,
        0xf4ef7175eedc7582, 0xf9fa8383dfe3908e, 0xfffe7a81f4ec707f, 0xdfe79b94e1ee836c,
        0x2027252418003d38, 0x4c42091d31193035, 0x5e560001422c180a, 0x6e6200004d320200,
        0x756c000e5f3c1001, 0x6f6c333f663e3f1d, 0x535b55395c293c1b, 0x2f1e3d5e22005300,
        0x004c0037004b001f, 0x00e000ca00be00ad, 0x02e30266018800eb, 0xffdcffeeffddfff3,
        0xfff9000700010007, 0xffe90003ffeefff4, 0x00000000fff5000d,
    };

    int EvalWeight(int item) => (int)(packedData[item >> 1] >> item * 32);

    public Move Think(Board boardOrig, Timer timerOrig) {
        maxSearchTime = timerOrig.MillisecondsRemaining / 4;

        board = boardOrig;
        timer = timerOrig;
        searchingDepth = 1;

        do
            //If score is of this value search has been aborted, DO NOT use result
            try {
                if (Abs(lastScore - Negamax(lastScore - 20, lastScore + 20, searchingDepth)) >= 20)
                    Negamax(-32000, 32000, searchingDepth);
                rootBestMove = searchBestMove;
                //Use for debugging, commented out because it saves a LOT of tokens!!
                //Console.WriteLine("info depth " + depth + " score cp " + score);
            } catch {
                break;
            }
        while (
            ++searchingDepth <= 200
                && searchingDepth <= maxDepth // #DEBUG
                && timerOrig.MillisecondsElapsedThisTurn < maxSearchTime / 10
        );

        return rootBestMove;
    }

    public int Negamax(int alpha, int beta, int depth) {
        //abort search
        if (timer.MillisecondsElapsedThisTurn >= maxSearchTime && searchingDepth > 1)
            throw null;

        //node count
        nodes++; // #DEBUG

        // check for game end
        if (board.IsInCheckmate())
            return board.PlyCount - 30000;
        if (board.IsDraw())
            return 0;

        ref var tt = ref transpositionTable[board.ZobristKey % 0x1000000];
        var (ttHash, ttMoveRaw, ttScore, ttDepth, ttBound) = tt;

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
            score = ttScore,
            tmp = 0;
        if (ttHit) {
            if (ttDepth >= depth && ttBound switch {
                65535 /* BOUND_LOWER */ => score >= beta,
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
                eval += (pieceIsWhite == board.IsWhiteToMove ? 1 : -1) * (
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
                );
                // phaseWeightTable = [0, 0, 1, 1, 2, 4, 0]
                tmp += 0x0421100 >> pieceType * 4 & 0xF;
            }
            // note: the correct way to extract EG eval is (eval + 0x8000) / 0x10000, but token count
            // the division is also moved outside Eval to save a token
            return (short)eval * tmp + eval / 0x10000 * (24 - tmp);
            // end tmp use
        }
        eval = ttHit ? score : Eval(board.AllPiecesBitboard) / 24;

        if (inQSearch)
            // stand pat in quiescence search
            alpha = Max(alpha, bestScore = eval);
        else if (nonPv && eval >= beta && board.TrySkipTurn()) {
            // Pruning based on null move observation
            bestScore = depth <= 3
                // RFP (66 elo, 10 tokens, 6.6 elo/token)
                ? eval - 51 * depth - 51
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
            (short)bestScore,
            (short)Max(depth, 0),
            (ushort)(
                bestScore >= beta
                    ? 65535 /* BOUND_LOWER */
                    : alpha - oldAlpha /* BOUND_UPPER if alpha == oldAlpha else BOUND_EXACT */
            )
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

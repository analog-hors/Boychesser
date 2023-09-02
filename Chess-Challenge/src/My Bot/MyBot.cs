using ChessChallenge.API;
using System;
using static System.Math;
using static ChessChallenge.API.BitboardHelper;

public class MyBot : IChessBot {

    public long nodes = 0; // #DEBUG
    public int maxSearchTime, searchingDepth;

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
        0x0000000000000000, 0x1b20170f26221401, 0x17172218211a1507, 0x1018291c251e1407,
        0x1e2630223032220f, 0x50534745665e321c, 0x86916f659ea25e4f, 0x0003000300070005,
        0xfffdfffd00050002, 0x2318011d190e2306, 0x1b140b17170d220d, 0x1314131b20141407,
        0x1c262123322b1f0b, 0x545a33427b642712, 0x86af5a44c1ba0f13, 0x0000000000000000,
        0x524b3230332b2a20, 0x5f584142514a322d, 0x7268524b604a4735, 0x7b795a576b5b5341,
        0x847d656a725d5452, 0x6f718a8163557351, 0x67606c7060484e3f, 0x5d69470359000000,
        0x2d2f242033292728, 0x2f2a222b2e2d2f2f, 0x3331252b2f36312c, 0x2a313326362e262f,
        0x2c2e3a3338362d2b, 0x29314140363c4239, 0x3935252c39342c21, 0x4646000042420109,
        0x6971554d71734947, 0x6c6c4b4a6b704437, 0x717246427170423e, 0x777a4b447b793c3e,
        0x7c7e5857807f504b, 0x7a7e726b7d7e6b5c, 0x82817a7c877e6165, 0x83837477827d6f76,
        0x8878545282905149, 0x897f58597b865654, 0x9b9f5053998f5455, 0xb9b04b4caca35254,
        0xc9c14c51bca75260, 0xcbcc5f5facab6d6c, 0xe6ce495dc1b44d5e, 0xb0b6756eadb65f4b,
        0x2128272619003f3b, 0x4d430b1f321a3237, 0x60570002432d1a0c, 0x6f6400004f340300,
        0x776e000d613d1001, 0x716e303d67403e1f, 0x555e53365e2a391c, 0x3120385a24005000,
        0x005400370053001f, 0x00f000d100d000ba, 0x034f025f01ab00f6, 0xffdcffeeffddfff3,
        0xfff9000700010007, 0xffe60003ffeefff4, 0x00000000fff5000d,
    };

    int EvalWeight(int item) => (int)(packedData[item / 2] >> item % 2 * 32);

    public Move Think(Board boardOrig, Timer timerOrig) {
        nodes = 0; // #DEBUG
        maxSearchTime = timerOrig.MillisecondsRemaining / 4;

        board = boardOrig;
        timer = timerOrig;
        searchingDepth = 1;

        do
            //If score is of this value search has been aborted, DO NOT use result
            try {
                Negamax(-32000, 32000, searchingDepth);
                rootBestMove = searchBestMove;
                //Use for debugging, commented out because it saves a LOT of tokens!!
                //Console.WriteLine("info depth " + depth + " score cp " + score);
            } catch (TimeoutException) {
                break;
            }
        while (++searchingDepth <= 200 && timerOrig.MillisecondsElapsedThisTurn < maxSearchTime / 10);

        return rootBestMove;
    }

    public int Negamax(int alpha, int beta, int depth) {
        //abort search
        if (timer.MillisecondsElapsedThisTurn >= maxSearchTime && searchingDepth > 1)
            throw new TimeoutException();

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
            eval = 0x000b000b, // tempo
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
            // Internal Iterative Reduction (IIR)
            depth--;

        int Eval(ulong pieces) {
            // use tmp as phase (initialized above)
            while (pieces != 0) {
                int pieceType, sqIndex;
                Piece piece = board.GetPiece(new(sqIndex = ClearAndGetIndexOfLSB(ref pieces)));
                pieceType = (int)piece.PieceType;
                // virtual pawn type
                // consider pawns on the opposite half of the king as distinct piece types (piece 0)
                pieceType -= (sqIndex % 8 ^ board.GetKingSquare(pieceIsWhite = piece.IsWhite).File) >> 1 >> pieceType;
                eval += (pieceIsWhite == board.IsWhiteToMove ? 1 : -1) * (
                    // material
                    EvalWeight(112 + pieceType)
                        // psts
                        + (int)(
                            packedData[pieceType * 8 + sqIndex / 8 ^ (pieceIsWhite ? 0 : 0b111)]
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
            // 3. quiets (no underpromotions, ordered by history)
            // 4. underpromotion quiets (ordered by knight, bishop, rook, tiebreak by history)
            scores[tmp++] -= ttHit && move.RawValue == ttMoveRaw ? 1000000
                : Max(
                    (int)move.CapturePieceType * 32768 - (int)move.MovePieceType - 16384,
                    HistoryValue(move) - (int)move.PromotionPieceType % 5 * 2048
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
        return bestScore;
    }

    ref int HistoryValue(Move move) => ref history[
        board.IsWhiteToMove ? 1 : 0,
        (int)move.MovePieceType,
        move.TargetSquare.Index
    ];
}

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
        0x0000000000000000, 0x2d25021a261b1b02, 0x24200f1623181c08, 0x1d1f161b291d1004,
        0x262a252736311e0a, 0x5a53364d72622d15, 0x8ca1594bbdb81514, 0x0002000300070005,
        0xfffdfffc00060001, 0x2122120e28241501, 0x1b1a1b15251d1508, 0x151b24192a241207,
        0x222f2b1c393a1f0d, 0x566843387b6e2915, 0x8aaa7058acab5e57, 0x0000000000000000,
        0x61593f3d413a372e, 0x6e684e4d60583e3a, 0x817760586f595443, 0x8a8868647a69604f,
        0x938c7378816b6361, 0x7d80998d71638260, 0x756e787e6f565a4d, 0x6b7756136d100000,
        0x3b3c2b2741372f31, 0x3c382a323b3b3737, 0x403e2d333d433835, 0x373e3b2e423b2e37,
        0x393b423c45433635, 0x353d4a4942484b42, 0x46432f33453f342d, 0x55560100504f0512,
        0x868f645b8f915855, 0x8a8c5956898e5145, 0x8f9054508e8e504c, 0x95985a5299964b4c,
        0x999b68659d9c605a, 0x979b8279999b7c6a, 0x9f9f8a88a49b6f74, 0xa0a084859e997f86,
        0xbcab7574b8ca736a, 0xbbaf7a7bacbd7a78, 0xc9ce7275c9c57777, 0xe5de6d6fded97476,
        0xf5f06f73eedd7582, 0xfbfc8181e0e38f8f, 0xffff797df6ea6d80, 0xdfe89b93e1ec836e,
        0x2025222617003a30, 0x4b42091b311a2d30, 0x5e560001432e1605, 0x6d6300004f340100,
        0x756d000d613f0e00, 0x6f6d323c69423c18, 0x555f5032612f3712, 0x322335562a004a00,
        0x004f002400490031, 0x00e000c700bf00ab, 0x02e50261018900e8, 0xffdafff3ffdfffee,
        0xfff9000700010008, 0xffe80003ffedfff4, 0x00000000fff70012,
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
                Negamax(-32000, 32000, searchingDepth, -30000);
                rootBestMove = searchBestMove;
                //Use for debugging, commented out because it saves a LOT of tokens!!
                //Console.WriteLine("info depth " + depth + " score cp " + score);
            } catch (TimeoutException) {
                break;
            }
        while (++searchingDepth <= 200 && timerOrig.MillisecondsElapsedThisTurn < maxSearchTime / 10);

        return rootBestMove;
    }

    public int Negamax(int alpha, int beta, int depth, int mateScore) {
        //abort search
        if (timer.MillisecondsElapsedThisTurn >= maxSearchTime && searchingDepth > 1)
            throw new TimeoutException();

        //node count
        nodes++; // #DEBUG

        // check for game end
        if (board.IsInCheckmate())
            return mateScore;
        if (board.IsDraw())
            return 0;
        mateScore++;

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
            moveCount = 0, // quietsToCheckTable = [0, 5, 8, 14, 49]
            quietsToCheck = 0b_110001_001110_001000_000101_000000 >> depth * 6 & 0b111111,

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
                Square square = new(sqIndex = ClearAndGetIndexOfLSB(ref pieces));
                Piece piece = board.GetPiece(square);
                pieceType = (int)piece.PieceType;
                // virtual pawn type
                // consider pawns on the opposite half of the king as distinct piece types (piece 0)
                pieceIsWhite = piece.IsWhite;
                pieceType -= square.File >> 1 >> pieceType;
                eval += (pieceIsWhite == board.IsWhiteToMove ? 1 : -1) * (
                    // material
                    EvalWeight(112 + pieceType)
                        // psts
                        + (int)(
                            packedData[pieceType * 8 + square.Rank ^ (pieceIsWhite ? 0 : 0b111)]
                                >> (0x01455410 >> sqIndex * 4) * 8
                                & 0xFF00FF
                        )
                        // mobility (35 elo, 19 tokens, 1.8 elo/token)
                        + EvalWeight(11 + pieceType) * GetNumberOfSetBits(
                            GetSliderAttacks((PieceType)Min(5, pieceType), square, board)
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
                ? eval - 44 * depth
                // Adaptive NMP (82 elo, 29 tokens, 2.8 elo/token)
                : -Negamax(-beta, -alpha, (depth * 96 + beta - eval) / 150 - 1, mateScore);
            board.UndoSkipTurn();
        }
        if (bestScore >= beta)
            return bestScore;

        var moves = board.GetLegalMoves(inQSearch);
        var scores = new int[moves.Length];
        // use tmp as scoreIndex
        tmp = 0;
        foreach (Move move in moves)
            // sort capture moves by MVV-LVA, quiets by history, and hashmove first
            scores[tmp++] -= ttHit && move.RawValue == ttMoveRaw ? 100000
                : Max((int)move.CapturePieceType * 4096 - (int)move.MovePieceType - 1024, HistoryValue(move));
        // end tmp use

        Array.Sort(scores, moves);
        Move bestMove = nullMove;
        foreach (Move move in moves) {
            // Delta pruning (23 elo, 21 tokens, 1.1 elo/token)
            // deltas = [208, 382, 440, 640, 1340]
            // due to sharing of the top bit of each entry with the bottom bit of the next one
            // (expands the range of values for the queen) all deltas must be even (except pawn)
            if (inQSearch && eval + (0b1_0100111100_1010000000_0110111000_0101111110_0011010000_0000000000 >> (int)move.CapturePieceType * 10 & 0b1_11111_11111) <= alpha)
                break;

            board.MakeMove(move);
            int
                nextDepth = board.IsInCheck() ? depth : depth - 1,
                reduction = (depth - nextDepth) * Max(
                    (moveCount * 120 + depth * 103) / 1000
                        // history reduction (5 elo, 4 tokens, 1.2 elo/token)
                        + scores[moveCount] / 256,
                    0
                );
            while (
                moveCount != 0
                    && (score = -Negamax(~alpha, -alpha, nextDepth - reduction, mateScore)) > alpha
                    && reduction != 0
            )
                reduction = 0;
            if (moveCount == 0 || score > alpha && score < beta)
                score = -Negamax(-beta, -alpha, nextDepth, mateScore);

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
                eval + 271 * depth < alpha
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

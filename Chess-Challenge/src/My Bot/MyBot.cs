using ChessChallenge.API;
using System;
using static System.Math;

// This struct should be 16 bytes large
struct TtEntry {
    public ulong hash;
    public ushort moveRaw;
    public short score, depth, bound /* BOUND_EXACT=1, BOUND_LOWER=2, BOUND_UPPER=3 */;
}

public class MyBot : IChessBot {

    public long nodes = 0;
    public int maxSearchTime, searchingDepth;

    public Timer timer;
    public Board board;

    Move nullMove, searchBestMove, rootBestMove;

    // Assuming the size of TtEntry is indeed 16 bytes, this table is precisely 256MiB.
    TtEntry[] transpositionTable = new TtEntry[0x1000000];

    short[,,] history = new short[2, 7, 64];

    ulong[] packedData = {
        0x0000000000000000, 0x29290e182e282002, 0x1e1d191a28202108, 0x1b21231f2f271904,
        0x283129213c3b2307, 0x56613b3275692c0d, 0x9ab45a49ccbc2739, 0x0000000000000000,
        0x696054504e384d42, 0x79715e5a7059504e, 0x8e846b637f66604f, 0x9c996d6c8a7b6b5f,
        0xa59e767a957e6c6f, 0x97989f9587799066, 0x9184898682685d5e, 0x879e6f0f86301b05,
        0x423f3031463e3235, 0x4345333543463d3d, 0x4d4b35394d4e3c3c, 0x4a503e344d493640,
        0x4c4f4741564d3b3e, 0x4b51554e56555441, 0x5855343c59553532, 0x6869130464601122,
        0x969b685f9d965a55, 0x9f9e58529c9d4f43, 0xa7a8534fa6a45248, 0xb0b45c59b2b05150,
        0xb8bb726bbfb96360, 0xb4ba9081b7be826d, 0xbdbc9a91c3bd7278, 0xb7b79496b7b39493,
        0x8a7f9a998aa69a91, 0x9989989d92a09d9e, 0xb5b99195afaa999d, 0xe2d3898fd2c1919b,
        0xf5f08e93ecc392a7, 0xfbfe9f9ae3d9ada6, 0xfef697a0fed98fa6, 0xd2d7bdbedddab1a5,
        0x1e26272a1a013f34, 0x4e440b19321a2b2e, 0x665d020149311208, 0x776a1211593f1200,
        0x837c26216f53230a, 0x8a962e1e8f641b1c, 0x89962d23a0510f25, 0x5a5e86815f05656d,
        0x00d80097004f0021, 0x01af00c4010a00ba, 0x00000000042701cd, 0x0002000300060004,
        0xfffcfffd00020002,
    };

    int EvalWeight(int item) => (int)(packedData[item / 2] >> item % 2 * 32);

    public Move Think(Board boardOrig, Timer timerOrig) {
        nodes = 0;
        maxSearchTime = timerOrig.MillisecondsRemaining / 4;

        board = boardOrig;
        timer = timerOrig;
        searchingDepth = 1;

        do
            //If score is of this value search has been aborted, DO NOT use result
            try {
                Negamax(-999999, 999999, searchingDepth, 0);
                rootBestMove = searchBestMove;
                //Use for debugging, commented out because it saves a LOT of tokens!!
                //Console.WriteLine("info depth " + depth + " score cp " + score);
            } catch (TimeoutException) {
                break;
            }
        while (++searchingDepth <= 200 && timerOrig.MillisecondsElapsedThisTurn < maxSearchTime / 10);

        return rootBestMove;
    }

    public int Negamax(int alpha, int beta, int depth, int nextPly) {
        //abort search
        if (timer.MillisecondsElapsedThisTurn >= maxSearchTime && searchingDepth > 1)
            throw new TimeoutException();

        //node count
        nodes++;

        // check for game end
        if (board.IsInCheckmate())
            return nextPly - 30000;
        nextPly++;

        ref var tt = ref transpositionTable[board.ZobristKey % 0x1000000];
        bool
            ttHit = tt.hash == board.ZobristKey,
            nonPv = alpha + 1 == beta;
        int
            bestScore = -99999,
            oldAlpha = alpha,

            // search loop vars
            moveCount = 0, // quietsToCheckTable = [0, 7, 8, 17, 49]
            quietsToCheck = 0b_110001_010001_001000_000111_000000 >> depth * 6 & 0b111111,

            // static eval vars
            pieceType,

            // temp vars
            score = tt.score,
            tmp = 0;

        if (ttHit && tt.depth >= depth && tt.bound switch {
            1 /* BOUND_EXACT */ => nonPv || depth <= 0,
            2 /* BOUND_LOWER */ => score >= beta,
            3 /* BOUND_UPPER */ => score <= alpha,
        })
            return score;

        // use tmp as phase (initialized above)
        // tempo
        score = 0x00000006;
        ulong pieces = board.AllPiecesBitboard;
        while (pieces != 0) {
            Square square = new(BitboardHelper.ClearAndGetIndexOfLSB(ref pieces));
            Piece piece = board.GetPiece(square);
            pieceType = (int)piece.PieceType - 1;
            score += (piece.IsWhite == board.IsWhiteToMove ? 1 : -1) * (
                // material
                EvalWeight(96 + pieceType)
                // psts
                    + (int)(
                        packedData[pieceType * 8 + square.Rank ^ (piece.IsWhite ? 0 : 0b111)]
                            >> (0x01455410 >> square.File * 4) * 8
                            & 0xFF00FF
                    )
                // mobility
                    + EvalWeight(100 + pieceType) * BitboardHelper.GetNumberOfSetBits(
                        BitboardHelper.GetSliderAttacks((PieceType)Min(5, pieceType+1), square, board)
                    )
            );
            // phase weight expression
            // maps 0 1 2 3 4 5 to 0 1 1 2 4 0
            tmp += (pieceType + 2 ^ 2) % 5;
        }
        score = ((short)score * tmp + (score + 0x8000) / 0x10000 * (24 - tmp)) / 24;
        // end tmp use

        if (depth <= 0) {
            // stand pat in quiescence search
            alpha = Max(alpha, bestScore = score);
            if (bestScore >= beta)
                return bestScore;
        } else if (nonPv && board.TrySkipTurn()) {
            // Null Move Pruning (NMP)
            score = depth < 4 ? score - 75 * depth : -Negamax(-beta, -alpha, depth - 3, nextPly);
            board.UndoSkipTurn();
            if (score >= beta)
                return score;
        }

        var moves = board.GetLegalMoves(depth <= 0);
        var scores = new int[moves.Length];
        // use tmp as scoreIndex
        tmp = 0;
        foreach (Move move in moves)
            // sort capture moves by MVV-LVA, quiets by history, and hashmove first
            scores[tmp++] -= ttHit && move.RawValue == tt.moveRaw ? 10000
                : move.IsCapture ? (int)move.CapturePieceType * 8 - (int)move.MovePieceType + 5000
                : HistoryValue(move);
        // end tmp use

        Array.Sort(scores, moves);
        Move bestMove = nullMove;
        foreach (Move move in moves) {
            //LMP
            if (nonPv && depth <= 4 && !move.IsCapture && quietsToCheck-- == 0)
                break;

            board.MakeMove(move);
            int nextDepth = board.IsInCheck() ? depth : depth - 1;
            if (board.IsDraw())
                score = 0;
            else if (moveCount == 0)
                score = -Negamax(-beta, -alpha, nextDepth, nextPly);
            else {
                // use tmp as reduction
                tmp = move.IsCapture || board.IsInCheck() ? 0
                    : (moveCount * 3 + depth * 4) / 40 + Convert.ToInt32(moveCount > 4);
                score = -Negamax(~alpha, -alpha, nextDepth - tmp, nextPly);
                if (score > alpha && tmp != 0)
                    score = -Negamax(~alpha, -alpha, nextDepth, nextPly);
                if (score > alpha && score < beta)
                    score = -Negamax(-beta, -alpha, nextDepth, nextPly);
                // end tmp use
            }

            board.UndoMove(move);

            if (score > bestScore) {
                alpha = Max(alpha, bestScore = score);
                bestMove = move;
            }
            if (score >= beta) {
                if (!move.IsCapture) {
                    // use tmp as change
                    tmp = depth * depth;
                    foreach (Move malusMove in moves.AsSpan(0, moveCount))
                        if (!malusMove.IsCapture)
                            HistoryValue(malusMove) -= (short)(tmp + tmp * HistoryValue(malusMove) / 4096);
                    HistoryValue(move) += (short)(tmp - tmp * HistoryValue(move) / 4096);
                    // end tmp use
                }
                break;
            }
            moveCount++;
        }

        tt.bound = (short)(bestScore >= beta ? 2 /* BOUND_LOWER */
            : alpha > oldAlpha ? 1 /* BOUND_EXACT */
            : 3 /* BOUND_UPPER */);
        tt.depth = (short)Max(depth, 0);
        tt.hash = board.ZobristKey;
        tt.score = (short)bestScore;
        if (!ttHit || tt.bound != 3 /* BOUND_UPPER */)
            tt.moveRaw = bestMove.RawValue;

        searchBestMove = bestMove;
        return bestScore;
    }

    ref short HistoryValue(Move move) => ref history[
        board.IsWhiteToMove ? 1 : 0,
        (int)move.MovePieceType,
        (int)move.TargetSquare.Index
    ];
}

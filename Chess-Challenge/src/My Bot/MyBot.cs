using ChessChallenge.API;
using System;
using static System.Math;
using static ChessChallenge.API.BitboardHelper;

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
        0x0000000000000000, 0x2b2b09152e291a04, 0x1f200f11241f1508, 0x1b211d1b2f271307,
        0x2d3626203e3d200d, 0x5a653e37796b2d15, 0x9ebb6550d3c52b42, 0x0000000000000000,
        0x6a64524d503a4941, 0x7b745c5a715c504e, 0x8e8369617b655f4e, 0x9a976d6c8877685d,
        0xa39c767a927c6b6c, 0x94959f9285798e65, 0x9084888481695957, 0x849c6c08872b1800,
        0x484532324c423339, 0x484a37384849403f, 0x4d4c363b4c513e3b, 0x485041374e4d3a41,
        0x4b4e484256503d40, 0x4b50585156585542, 0x5c59373c5a583631, 0x6b6d140268671021,
        0x989f6e65a29d615e, 0xa2a15f5a9fa0554c, 0xa7aa5b58a6a55c53, 0xb1b5615fb6b35958,
        0xb7bd7972bfbc6b67, 0xb5bc9688bbc18874, 0xbebf9d96c7c1787c, 0xbbbb999abcba9896,
        0x857b9494869d948d, 0x9686959a8b979898, 0xb0b58f93a8a29799, 0xe0d1868eccbb8f96,
        0xf6f08b8ee7bf8ea3, 0xfbfb9b98e0d1a9a0, 0xfff5929cf9d7889c, 0xced5bbb8ddddaa9a,
        0x1e241e201901372d, 0x4e450513321a262a, 0x61580407442f1506, 0x72661315553c1400,
        0x7d782824694e2a07, 0x848f3222885f2019, 0x818e3124974d1028, 0x5355888259016b68,
        0x00d9009a005d002c, 0x01ba00ce010c00bf, 0x00000000044401df, 0x0002000300060004,
        0xfffcfffd00020002, 0x00020003ffeffffa, 0xfff4fff4fffb0003, 0xfffe000bfff30000,
        0x00090000000b0006, 0x000afffd000c0002, 0x0015ffde0006fffe,
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
        score = 0x00010006;
        ulong pieces = board.AllPiecesBitboard;
        while (pieces != 0) {
            Square square = new(ClearAndGetIndexOfLSB(ref pieces));
            Piece piece = board.GetPiece(square);
            pieceType = (int)piece.PieceType - 1;
            ulong pawns = board.GetPieceBitboard(PieceType.Pawn, piece.IsWhite);
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
                    + EvalWeight(100 + pieceType) * GetNumberOfSetBits(
                        GetSliderAttacks((PieceType)Min(5, pieceType+1), square, board)
                    )
                // own pawn on file
                    + EvalWeight(106 + pieceType) * GetNumberOfSetBits(
                        0x0101010101010101UL << square.File & pawns
                    )
                // pawn support
                    + EvalWeight(112 + pieceType) * GetNumberOfSetBits(
                        GetPawnAttacks(square, !piece.IsWhite) & pawns
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

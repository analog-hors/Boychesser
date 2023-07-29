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
        0x0000000000000000, 0x30310c16332c1b04, 0x252616182d241d0b, 0x2127201d362b1508,
        0x313a2923443f220d, 0x5e693f387d6e2e14, 0xa0bd6551d5c62c42, 0x0000000000000000,
        0x6962524d4f394a42, 0x79735d5a705b514f, 0x8f856a627e67604f, 0x9b9a6e6c8979695f,
        0xa69e767a947d6b6c, 0x95969f92867a8e66, 0x9084888581695958, 0x849c6c08872c1900,
        0x464330304a403237, 0x4548343646473e3e, 0x4e4d353a4f503e3a, 0x4a523f35504c3840,
        0x4e504640594f3b3e, 0x4c52554e5857523f, 0x5b5934395b57332e, 0x696b120167660e20,
        0x949b6e669e9a615e, 0x9e9d5f5a9c9d554d, 0xa5a85b57a5a35b53, 0xaeb3615eb4b05858,
        0xb6bc7871beb96967, 0xb3ba9587babe8774, 0xbcbc9d96c5bf787c, 0xb8b9989abab89896,
        0x847a9595869d968f, 0x9485969b8a989999, 0xb1b68f92aba49799, 0xe0d1868ecdbb8f97,
        0xf6ef8a8ee7be8ea3, 0xfbfc9b97e1d1a8a1, 0xfff5929cfad7899c, 0xced5bbb9dddcab9b,
        0x1d24262719013e34, 0x4d450c19321b2c30, 0x635b02044831130a, 0x74691314583f1400,
        0x807a27246e4f260a, 0x869234238c611e1c, 0x849032269b500f2a, 0x575989815d026872,
        0x00d90099005f002e, 0x01bb00cd010e00bf, 0x00000000043f01db, 0x0002000300050004,
        0xfffcfffd00010002, 0x00030003ffecfff9, 0xfff3fff4fffb0003, 0xfffd000bfff20000,
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
                // own pawn on file
                    + EvalWeight(106 + pieceType) * BitboardHelper.GetNumberOfSetBits(
                        0x0101010101010101UL << square.File
                            & board.GetPieceBitboard(PieceType.Pawn, piece.IsWhite)
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

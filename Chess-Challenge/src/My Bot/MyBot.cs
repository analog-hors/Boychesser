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

    public long nodes = 0; // #DEBUG
    public int maxSearchTime, searchingDepth;

    public Timer timer;
    public Board board;

    Move nullMove, searchBestMove, rootBestMove;

    // Assuming the size of TtEntry is indeed 16 bytes, this table is precisely 256MiB.
    TtEntry[] transpositionTable = new TtEntry[0x1000000];

    int[,,] history = new int[2, 7, 64];

    ulong[] packedData = {
        0x0000000000000000, 0x2e2e0b173028210f, 0x24231519291f2416, 0x1f251f1e32261c13,
        0x2f382724413a2919, 0x5c663d3b7a693421, 0x9db96557d2c13451, 0x0000000000000000,
        0x635c4f4a4a33473f, 0x746d59576a554e4c, 0x897e665e77605d4d, 0x959369698373655d,
        0xa09871778e77686a, 0x8e909b9081748a64, 0x887e86827b645857, 0x7e96670482261500,
        0x484733314f453137, 0x484b3637494c3f3e, 0x5150373c53553f3a, 0x4d55403654503940,
        0x515347415d543c3c, 0x4f55564f5c5b513f, 0x5e5b353b5f5c352e, 0x6c6f12016b6a0e21,
        0x969b6b649f9c5a55, 0x9e9d5e5a9c9e5144, 0xa5a75956a6a5574c, 0xafb35f5db5b25450,
        0xb6bc766fc0bb645f, 0xb3bb9284bbc0806c, 0xbcbd9b93c7c17374, 0xb8b99696bbba918c,
        0x857b9493879a928a, 0x968895988d969694, 0xb4b98e91ada39496, 0xe1d4868ccdb88d94,
        0xf8f18a8ce8bd8ba0, 0xfafa9c97e0cfa79e, 0xfff5929cf8d4899b, 0xd0d5b9b6dcdaa797,
        0x2e301d2a20015266, 0x5a4f071d381b425f, 0x6e65010a4f352730, 0x7d72171c6046251d,
        0x89842e2e77583525, 0x8f9b3f2f966a2c33, 0x8c994536a55a1e40, 0x5e61a399660b7f8e,
        0x00e000a5005f0041, 0x01bd00ce010d00c1, 0x00000000043b01e1, 0x0002000200050004,
        0xfffcfffd00020002, 0x00020001ffedfff8, 0xfff4fff4fffa0003, 0xfffdfffefff50000,
        0xfffffffe0000fff9, 0xffff0003ffff0000, 0x0000000000010001,
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
        nodes++; // #DEBUG

        // check for game end
        if (board.IsInCheckmate())
            return nextPly - 30000;
        if (board.IsDraw())
            return 0;
        nextPly++;

        ref var tt = ref transpositionTable[board.ZobristKey % 0x1000000];
        bool
            ttHit = tt.hash == board.ZobristKey,
            nonPv = alpha + 1 == beta,
            inQSearch = depth <= 0;
        int
            bestScore = -99999,
            oldAlpha = alpha,

            // search loop vars
            moveCount = 0, // quietsToCheckTable = [0, 8, 7, 16, 50]
            quietsToCheck = 0b_110010_010000_000111_001000_000000 >> depth * 6 & 0b111111,

            // static eval vars
            pieceType,

            // temp vars
            score = tt.score,
            tmp = 0;

        if (ttHit && tt.depth >= depth && tt.bound switch {
            1 /* BOUND_EXACT */ => nonPv || inQSearch,
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
                        BitboardHelper.GetSliderAttacks((PieceType)Min(5, pieceType + 1), square, board)
                    )
                    // own pawn on file
                    + EvalWeight(106 + pieceType) * BitboardHelper.GetNumberOfSetBits(
                        0x0101010101010101UL << square.File
                            & board.GetPieceBitboard(PieceType.Pawn, piece.IsWhite)
                    )
                    // king file distance
                    + EvalWeight(112 + pieceType) * Abs(square.File - board.GetKingSquare(piece.IsWhite).File)
            );
            // phaseWeightTable = [0, 1, 1, 2, 4, 0]
            tmp += 0x042110 >> pieceType * 4 & 0xF;
        }
        score = ((short)score * tmp + (score + 0x8000) / 0x10000 * (24 - tmp)) / 24;
        // end tmp use

        if (inQSearch) {
            // stand pat in quiescence search
            alpha = Max(alpha, bestScore = score);
        } else if (nonPv && board.TrySkipTurn()) {
            // Null Move Pruning (NMP)
            score = depth < 4 ? score - 42 * depth : -Negamax(-beta, -alpha, depth - 4, nextPly);
            board.UndoSkipTurn();
        } else goto afterNullMoveObservation;
        if (score >= beta)
            return score;
        afterNullMoveObservation:

        var moves = board.GetLegalMoves(inQSearch);
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
            if (nonPv && depth <= 4 && !move.IsCapture && (quietsToCheck-- == 0 || scores[moveCount] > 256 && moveCount != 0))
                break;

            board.MakeMove(move);
            int nextDepth = board.IsInCheck() ? depth : depth - 1;
            if (moveCount == 0)
                score = -Negamax(-beta, -alpha, nextDepth, nextPly);
            else {
                // use tmp as reduction
                tmp = move.IsCapture || nextDepth >= depth ? 0
                    : (moveCount * 59 + depth * 109) / 1000 + Min(moveCount / 6, 1);
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
                            HistoryValue(malusMove) -= tmp + tmp * HistoryValue(malusMove) / 512;
                    HistoryValue(move) += tmp - tmp * HistoryValue(move) / 512;
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

    ref int HistoryValue(Move move) => ref history[
        board.IsWhiteToMove ? 1 : 0,
        (int)move.MovePieceType,
        (int)move.TargetSquare.Index
    ];
}

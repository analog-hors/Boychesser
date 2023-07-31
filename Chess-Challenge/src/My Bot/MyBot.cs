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
        0x0000000000000000, 0x34340c1837261d03, 0x282913152d1d1a08, 0x242a211f38251707,
        0x363f2b25473b250d, 0x636e443d836a3216, 0xa6c46b56ddc33045, 0x0000000000000000,
        0x433e4641270b3d37, 0x605a4d4b4c334341, 0x7e72554d61424c3e, 0x89865a596d53584f,
        0x948c626777595b5f, 0x85868e816c567f5a, 0x776c7a775e424d4d, 0x627761005f000f00,
        0x585544435b4f4548, 0x5d5e4e4f5b575650, 0x68654e536360524d, 0x676b5d4f655c5054,
        0x6968635b6d605253, 0x646a71696b686b55, 0x716e4e526e664d44, 0x7c7d281578762432,
        0xaab07b74b3af6d68, 0xb5b46c67b2b26257, 0xbcbf6461bbb9645c, 0xc5ca6c68cac66362,
        0xcbd2837cd3cf7572, 0xc8d0a294cfd4937f, 0xd2d2aba3dad48588, 0xcecea5a6cfcda3a1,
        0x8379bcbd849dbcb3, 0x9587c0c48996c2be, 0xb5b8b6baaba2bdbe, 0xe2d1b1b8ccb8b8be,
        0xf7f0b7b9e5bdb7cb, 0xfbfbc7c3dfcfd3c8, 0xfff4bdc7f8d5b2c4, 0xcfd5e2dfdcdcd0c0,
        0x1b232e3319014b45, 0x4b42101f301a353d, 0x5f570103432e1614, 0x6f631113543d1703,
        0x7a762523694e290e, 0x828d3323885f2120, 0x818d3328964f1530, 0x54568d875a027071,
        0x011100a1006a0031, 0x01b000ca010d00b5, 0x00000000045201c0, 0xfffa0001fff4fffc,
        0x0001000200030003, 0xfffdfffd00010001, 0x00020003ffeefffa, 0xfff4fff4fffc0004,
        0xfffe000cfff30000,
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
        score = 0x00020006;
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
                    + EvalWeight(102 + pieceType) * BitboardHelper.GetNumberOfSetBits(
                        BitboardHelper.GetPieceAttacks((PieceType)Min(5, pieceType + 1), square, board, piece.IsWhite) & ~board.AllPiecesBitboard
                    )
                    // own pawn on file
                    + EvalWeight(108 + pieceType) * BitboardHelper.GetNumberOfSetBits(
                        0x0101010101010101UL << square.File
                            & board.GetPieceBitboard(PieceType.Pawn, piece.IsWhite)
                    )
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

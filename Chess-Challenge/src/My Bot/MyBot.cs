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
        0x0000000000000000, 0x35350b1838281c03, 0x282914162d1e1b0a, 0x252b221f39251708,
        0x353f2c26483b250e, 0x636e443d836a3116, 0xa7c46c56ddc43045, 0x0000000000000000,
        0x453f454029103c36, 0x5a534e4b4c354340, 0x716657505a404d3f, 0x7c7a5c5b6752594f,
        0x877f656971585b5e, 0x7778908364557f59, 0x6f647b775d434c4b, 0x60765f0061000c00,
        0x5b5846455d514649, 0x5c5e4f505a585750, 0x65634f536261544e, 0x62675b4f635d5054,
        0x6565625b6c615254, 0x616771696a696b55, 0x6f6c4d526d684c44, 0x7c7e251379782333,
        0xa8ae7c74b1ac6d69, 0xb3b26d67afb06258, 0xb9bc6562b7b6645d, 0xc1c56c68c6c26362,
        0xc6cd817acfcb7371, 0xc4cba091cbd0907d, 0xcdcda8a0d6d08387, 0xcacaa3a4cbc9a2a0,
        0x887db2b287a0b2a9, 0x9788b6ba8b99b8b4, 0xb4b8acb0aca4b2b3, 0xe1d0a4acccbbacb2,
        0xf6efa9abe5bea9be, 0xfafbb9b4e0d2c5bb, 0xfff5aeb8f9d8a3b6, 0xd0d6d4d1dedec2b3,
        0x1b232c3019014842, 0x4a420e1d301b333b, 0x5e560104432f1512, 0x6d631315543e1702,
        0x79752725694f290e, 0x818d352488602121, 0x808d3629974f1531, 0x54568f895b017275,
        0x010600a500690031, 0x01aa00ca010500b4, 0x00000000044201ca, 0xfffe0001fff4fffd,
        0x0002000200040003, 0xfffdfffe00020001, 0x00020003ffeefff9, 0xfff6fff4fffb0003,
        0xfffe000bfff50000,
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
                    + EvalWeight(102 + pieceType) * BitboardHelper.GetNumberOfSetBits(
                        BitboardHelper.GetPieceAttacks((PieceType)Min(5, pieceType + 1), square, board, piece.IsWhite) & ~(piece.IsWhite ? board.WhitePiecesBitboard : board.BlackPiecesBitboard)
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

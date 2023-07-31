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

    public long nodes = 0; // #DEBUG
    public int maxSearchTime, searchingDepth;

    public Timer timer;
    public Board board;

    Move nullMove, searchBestMove, rootBestMove;

    // Assuming the size of TtEntry is indeed 16 bytes, this table is precisely 256MiB.
    TtEntry[] transpositionTable = new TtEntry[0x1000000];

    int[,,] history = new int[2, 7, 64];

    ulong[] packedData = {
        0x0000000000000000, 0x30310a16322b1b04, 0x252615172c231d0b, 0x22281f1d362a1508,
        0x353d2923463f220d, 0x636e3e39806f2e14, 0xa2bd6754d6c52e44, 0x0000000000000000,
        0x6660544f4c364c44, 0x78715f5c6e595351, 0x8e836c647c646251, 0x9b996f6d88776a60,
        0xaba16e76967d666a, 0x9a99958d897a8661, 0x9385828180685956, 0x849a6906842a1a00,
        0x474532314b413338, 0x474936374749403e, 0x4f4e373c51523f3a, 0x4c534036514d3940,
        0x5052443e5b513a3b, 0x4e53514c5a5a4f3a, 0x5b5934395b57352e, 0x696c120168660f21,
        0x999f7067a29e6360, 0xa2a2615ba0a0574f, 0xa9ac5c58a9a75b55, 0xb2b7635fb7b3595a,
        0xbac07972c2bd6a69, 0xb7bf9586bec28674, 0xc2c29891cbc4747a, 0xbdbd9496bebc9594,
        0x9488979691a79790, 0xa592989d94a19b9a, 0xbfc19194b4ad999a, 0xe8d9878fd4c19099,
        0xfbf5898cefc48ea0, 0xfafb9796e7d3a49c, 0xfff38b98fad6889b, 0xc2cbb2b2d4d0a79a,
        0x1c231c1e18013428, 0x453d0a162c192a28, 0x534a070a3b2b1804, 0x605614164a361500,
        0x6f6a242261492302, 0x7a843327825c1f15, 0x7985372c914d1423, 0x5152857f5701635d,
        0x00de00970062002e, 0x01bb00cd010e00bf, 0x00000000043801e4, 0x0002000200060004,
        0xfffdfffd00010002, 0x00020002ffecfff9, 0xfff2fff5fffa0003, 0x0000000affef0000,
        0xfff8000afff60002, 0xfffc0009fffd0009, 0x00000000000d0006,
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
            Square square = new(ClearAndGetIndexOfLSB(ref pieces));
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
                    + EvalWeight(100 + pieceType) * GetNumberOfSetBits(
                        GetSliderAttacks((PieceType)Min(5, pieceType + 1), square, board)
                    )
                    // own pawn on file
                    + EvalWeight(106 + pieceType) * GetNumberOfSetBits(
                        0x0101010101010101UL << square.File
                            & board.GetPieceBitboard(PieceType.Pawn, piece.IsWhite)
                    )
                    // king ring attacks
                    + EvalWeight(112 + pieceType) * GetNumberOfSetBits(
                        GetPieceAttacks(piece.PieceType, square, board, piece.IsWhite) & GetKingAttacks(board.GetKingSquare(!piece.IsWhite))
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

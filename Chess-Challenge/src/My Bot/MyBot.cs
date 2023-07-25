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

    // Every 2nd 4th and 5th element is negated to save tokens
    int[] constants = {
        8257607, 18809057, 22085864, 38142210, 77726293, 0,
        1638405, 327688, 262146, 327690, 1048579, 983045,
        -196606, 655367, -65536, -65526, 524287, 1179621,
        -1900547, 1048576, 131072, 393203, 1048569, 655333,
        0, 0, 327685, 196612, 65539, -196610,
        65529, -65538, -65536, -131068, 196608, 0,
    };


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
        bool tt_good = tt.hash == board.ZobristKey;
        bool nonPv = alpha + 1 == beta;

        if (tt_good && tt.depth >= depth && (
            tt.bound == 1 /* BOUND_EXACT */ && (nonPv || depth <= 0) ||
            tt.bound == 2 /* BOUND_LOWER */ && tt.score >= beta ||
            tt.bound == 3 /* BOUND_UPPER */ && tt.score <= alpha
        ))
            return tt.score;

        // Null Move Pruning (NMP)
        if (nonPv && depth >= 1 && board.TrySkipTurn()) {
            var result = -Negamax(-beta, 1 - beta, depth - 3, nextPly);
            board.UndoSkipTurn();
            if (result >= beta)
                return result;
        }

        int bestScore = -999999;
        bool raisedAlpha = false;

        // static eval for qsearch
        if (depth <= 0) {
            int staticEval = 0, phase = 0, pieceIndex = 0;
            foreach (PieceList pieceList in board.GetAllPieceLists()) {
                int pieceType = pieceIndex % 6;
                // Maps 0, 1, 2, 3, 4, 5 -> 0, 1, 1, 2, 4, 0 for pieceType
                phase += pieceType * pieceType * 21 % 26 % 5 * pieceList.Count;
                bool white = pieceIndex < 6;
                int negate = white == board.IsWhiteToMove ? 1 : -1;
                foreach (Piece piece in pieceList) {
                    Square square = piece.Square;
                    int y = white ? square.Rank : 7 - square.Rank;
                    staticEval += negate * (
                        constants[pieceType]
                        + y * constants[6 + pieceType]
                        + Min(square.File, 7 - square.File) * constants[12 + pieceType]
                        + Min(y, 7 - y) * constants[18 + pieceType]
                        + constants[24 + pieceType] * BitboardHelper.GetNumberOfSetBits(
                            BitboardHelper.GetSliderAttacks(
                                (PieceType)Min(5, pieceType + 1), square, board)
                            )
                        + constants[30 + pieceType] * Abs(square.File - board.GetKingSquare(white).File));
                }
                pieceIndex++;
            }
            staticEval = ((short)staticEval * phase + (staticEval + 0x8000) / 0x10000 * (24 - phase)) / 24;
            if (staticEval >= beta)
                return staticEval;

            if (raisedAlpha = staticEval > alpha)
                alpha = staticEval;
            bestScore = staticEval;
        }

        var moves = board.GetLegalMoves(depth <= 0);
        var scores = new int[moves.Length];
        int scoreIndex = 0;
        foreach (Move move in moves)
            // sort capture moves by MVV-LVA, quiets by history, and hashmove first
            scores[scoreIndex++] = -(tt_good && move.RawValue == tt.moveRaw ? 10000
                : move.IsCapture ? (int)move.CapturePieceType * 8 - (int)move.MovePieceType + 5000
                : HistoryValue(move));

        Array.Sort(scores, moves);
        Move bestMove = nullMove;
        // quietsToCheckTable = [0, 7, 8, 17, 49]
        int moveCount = 0, quietsToCheck = 0b_110001_010001_001000_000111_000000 >> depth * 6 & 0b111111, score;
        foreach (Move move in moves) {
            if (move.IsPromotion && move.PromotionPieceType != PieceType.Queen) continue;
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
                int reduction = move.IsCapture || board.IsInCheck() ? 0
                    : (moveCount * 3 + depth * 4) / 40 + Convert.ToInt32(moveCount > 4);
                score = -Negamax(-alpha - 1, -alpha, nextDepth - reduction, nextPly);
                if (score > alpha && reduction != 0)
                    score = -Negamax(-alpha - 1, -alpha, nextDepth, nextPly);
                if (score > alpha && score < beta)
                    score = -Negamax(-beta, -alpha, nextDepth, nextPly);
            }

            board.UndoMove(move);

            if (score > bestScore) {
                bestScore = score;
                bestMove = move;
            }
            if (score >= beta) {
                if (!move.IsCapture) {
                    int change = depth * depth;
                    foreach (Move malusMove in moves.AsSpan(0, moveCount))
                        if (!malusMove.IsCapture && (!move.IsPromotion || move.PromotionPieceType == PieceType.Queen))
                            HistoryValue(malusMove) -= (short)(change + change * HistoryValue(malusMove) / 4096);
                    HistoryValue(move) += (short)(change - change * HistoryValue(move) / 4096);
                }
                break;
            }
            if (score > alpha) {
                raisedAlpha = true;
                alpha = score;
            }
            moveCount++;
        }

        tt.bound = (short)(bestScore >= beta ? 2 /* BOUND_LOWER */
            : raisedAlpha ? 1 /* BOUND_EXACT */
            : 3 /* BOUND_UPPER */);
        tt.depth = (short)Max(depth, 0);
        tt.hash = board.ZobristKey;
        tt.score = (short)bestScore;
        if (!tt_good || tt.bound != 3 /* BOUND_UPPER */)
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

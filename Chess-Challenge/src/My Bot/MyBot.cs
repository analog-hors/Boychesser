using ChessChallenge.API;
using System;

// This struct should be 14 bytes large, which with padding means 16 bytes
struct TtEntry {
    public ulong hash;
    public short score;
    public ushort moveRaw;
    public byte depth, bound /* BOUND_EXACT=1, BOUND_LOWER=2, BOUND_UPPER=3 */;
}

public class MyBot : IChessBot {

    public long nodes = 0;
    public int maxSearchTime;

    // Assuming the size of TtEntry is indeed 16 bytes, this table is precisely 256MiB.
    TtEntry[] transposition_table = new TtEntry[0x1000000];

    short[,] history = new short[7, 64];

    public Move Think(Board board, Timer timer) {
        nodes = 0;
        maxSearchTime = timer.MillisecondsRemaining / 80;

        Move best = Move.NullMove;

        for (int depth = 1; depth <= 200; depth++) {
            //If score is of this value search has been aborted, DO NOT use result
            try {
                (int score, Move bestMove) = Negamax(board, -999999, 999999, depth, timer, depth, 0);
                best = bestMove;
                //Use for debugging, commented out because it saves a LOT of tokens!!
                //Console.WriteLine("info depth " + depth + " score cp " + score);
            } catch (Exception) {
                break;
            }
        }

        return best;
    }

    public (int, Move) Negamax(Board board, int alpha, int beta, int depth, Timer timer, int searchingDepth, int ply) {
        //abort search
        if (timer.MillisecondsElapsedThisTurn >= maxSearchTime && searchingDepth > 1) {
            throw new Exception();
        }

        //node count
        nodes++;

        // check for game end
        if (board.IsInCheckmate()) {
            return (-30000, Move.NullMove);
        }

        ref var tt = ref transposition_table[board.ZobristKey % 0x1000000];
        bool tt_good = tt.hash == board.ZobristKey;
        bool pv = alpha > beta + 1;

        if (tt_good && tt.depth >= depth && ply > 0) {
            if (tt.bound == 1 /* BOUND_EXACT */ ||
                    tt.bound == 2 /* BOUND_LOWER */ && tt.score >= beta ||
                    tt.bound == 3 /* BOUND_UPPER */ && tt.score <= alpha) {
                return (tt.score, Move.NullMove);
            }
        }

        int bestScore = -999999;
        bool raisedAlpha = false;

        // static eval for qsearch
        int staticEval;
        if (depth <= 0) {
            PieceList[] pieceLists = board.GetAllPieceLists();
            staticEval = (pieceLists[0].Count - pieceLists[6].Count)
                + 3 * (pieceLists[1].Count + pieceLists[2].Count - pieceLists[7].Count - pieceLists[8].Count)
                + 5 * (pieceLists[3].Count - pieceLists[9].Count)
                + 9 * (pieceLists[4].Count - pieceLists[10].Count);
            staticEval = board.IsWhiteToMove ? staticEval : -staticEval;
            if (staticEval >= beta) {
                return (staticEval, Move.NullMove);
            }
            if (staticEval > alpha) {
                raisedAlpha = true;
                alpha = staticEval;
            }
            bestScore = staticEval;
        }

        var moves = board.GetLegalMoves(depth <= 0);
        var scores = new int[moves.Length];
        for (int i = 0; i < moves.Length; i++) {
            // sort capture moves by MVV-LVA, quiets by history, and hashmove first
            scores[i] = tt_good && moves[i].RawValue == tt.moveRaw ? 10000
                : moves[i].CapturePieceType == 0 ? HistoryValue(moves[i])
                : (int)moves[i].CapturePieceType * 8 - (int)moves[i].MovePieceType + 5000;
            scores[i] *= -1;
        }

        Array.Sort(scores, moves);
        Move bestMove = Move.NullMove;
        for (int i = 0; i < moves.Length; i++) {
            Move move = moves[i];
            board.MakeMove(move);
            int score;
            if (board.IsDraw()) {
                score = 0;
            } else if (i == 0) {
                score = -Negamax(board, -beta, -alpha, depth - 1, timer, searchingDepth, ply + 1).Item1;
            } else {
                score = -Negamax(board, -alpha - 1, -alpha, depth - 1, timer, searchingDepth, ply + 1).Item1;
                if (score > alpha && score < beta) {
                    score = -Negamax(board, -beta, -alpha, depth - 1, timer, searchingDepth, ply + 1).Item1;
                }
            }

            board.UndoMove(move);

            if (score > bestScore) {
                bestScore = score;
                bestMove = move;
            }
            if (score >= beta) {
                int change = depth * depth;
                for (int j = 0; j < i; j++) {
                    HistoryValue(moves[j]) -= (short)(change + change * HistoryValue(moves[j]) / 4096);
                }
                HistoryValue(move) += (short)(change - change * HistoryValue(move) / 4096);
                break;
            }
            if (score > alpha) {
                raisedAlpha = true;
                alpha = score;
            }
        }

        tt.bound = (byte)(bestScore >= beta ? 2 /* BOUND_LOWER */
            : raisedAlpha ? 1 /* BOUND_EXACT */
            : 3 /* BOUND_UPPER */);
        tt.depth = (byte)(depth < 0 ? 0 : depth);
        tt.hash = board.ZobristKey;
        tt.score = (short)bestScore;
        tt.moveRaw = bestMove.RawValue;

        return (bestScore, bestMove);
    }

    ref short HistoryValue(Move move) {
        return ref history[(int)move.MovePieceType, (int)move.TargetSquare.Index];
    }
}
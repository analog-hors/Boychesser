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

    public int searchingDepth;

    public Timer timer;
    public Board board;

    Move nullMove = Move.NullMove;

    // Assuming the size of TtEntry is indeed 16 bytes, this table is precisely 256MiB.
    TtEntry[] transposition_table = new TtEntry[0x1000000];

    short[,,] history = new short[2, 7, 64];

    // 3rd and 4th rows are negated to save tokens
    int[] constants = {
        2752554, 17432802, 18809083, 30081304, 57541292, 196608,
        589829, 327689, 196612, 196617, 917507, 655358,
        -65534, 393219, 196609, 131078, 196608, 65532,
        -983041, 786431, 327684, 196597, 393216, 393191,
        196608, -65538, 0, 196605, -327681, -65549,
        0, 0, 0, 0, 0, 0
    };


    public Move Think(Board boardOrig, Timer timerOrig) {
        nodes = 0;
        maxSearchTime = timerOrig.MillisecondsRemaining / 4;
        int targetSearchTime = maxSearchTime / 10;

        Move best = nullMove, searchMove = nullMove;


        board = boardOrig;
        timer = timerOrig;
        searchingDepth = 0;

        while (++searchingDepth <= 200 && timerOrig.MillisecondsElapsedThisTurn < targetSearchTime)
            //If score is of this value search has been aborted, DO NOT use result
            try {
                Negamax(-999999, 999999, searchingDepth, 0, ref searchMove);
                best = searchMove;
                //Use for debugging, commented out because it saves a LOT of tokens!!
                //Console.WriteLine("info depth " + depth + " score cp " + score);
            } catch (Exception) {
                break;
            }

        return best;
    }

    public int Negamax(int alpha, int beta, int depth, int ply, ref Move outMove) {
        //abort search
        if (timer.MillisecondsElapsedThisTurn >= maxSearchTime && searchingDepth > 1) {
            throw new Exception();
        }

        //node count
        nodes++;

        // check for game end
        if (board.IsInCheckmate()) {
            return -30000;
        }

        ref var tt = ref transposition_table[board.ZobristKey % 0x1000000];
        bool tt_good = tt.hash == board.ZobristKey;
        bool nonPv = alpha + 1 == beta;

        if (tt_good && tt.depth >= depth && ply > 0) {
            if (tt.bound == 1 /* BOUND_EXACT */ ||
                    tt.bound == 2 /* BOUND_LOWER */ && tt.score >= beta ||
                    tt.bound == 3 /* BOUND_UPPER */ && tt.score <= alpha) {
                return tt.score;
            }
        }

        // Null Move Pruning (NMP)
        if (nonPv && depth >= 1) {
            if (board.TrySkipTurn()) {
                var result = Negamax(-beta, 1 - beta, depth - 3, ply + 1, ref outMove);
                board.UndoSkipTurn();
                if (-result >= beta) {
                    return -result;
                }
            }
        }

        int bestScore = -999999;
        bool raisedAlpha = false;

        // static eval for qsearch
        int staticEval = 0, phase = 0, pieceIndex = 0;
        if (depth <= 0) {
            foreach (PieceList pieceList in board.GetAllPieceLists()) {
                int pieceType = pieceIndex % 6;
                // Maps 0, 1, 2, 3, 4, 5 -> 0, 1, 1, 2, 4, 0 for pieceType
                phase += pieceType * pieceType * 21 % 26 % 5 * pieceList.Count;
                bool reverse = pieceIndex >= 6;
                foreach (Piece piece in pieceList) {
                    Square king = board.GetKingSquare(reverse);
                    Square square = piece.Square;
                    int y = reverse ? 7 - square.Rank : square.Rank;
                    staticEval += (constants[pieceType]
                    + y * constants[6 + pieceType]
                    - Math.Abs(square.File - 3) * constants[12 + pieceType]
                    - Math.Abs(y - 3) * constants[18 + pieceType]
                    + Math.Abs(square.File - king.File) * constants[24 + pieceType]
                    + Math.Abs(y - king.Rank) * constants[30 + pieceType]) * (reverse ? -1 : 1);
                }
                pieceIndex++;
            }
            staticEval = board.IsWhiteToMove ? staticEval : -staticEval;
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
        foreach (Move move in moves) {
            // sort capture moves by MVV-LVA, quiets by history, and hashmove first
            scores[scoreIndex++] = -(tt_good && move.RawValue == tt.moveRaw ? 10000
                : move.CapturePieceType == 0 ? HistoryValue(move)
                : (int)move.CapturePieceType * 8 - (int)move.MovePieceType + 5000);
        }

        Array.Sort(scores, moves);
        Move bestMove = nullMove;
        int moveCount = 0, score;
        foreach (Move move in moves) {
            board.MakeMove(move);
            if (board.IsDraw()) {
                score = 0;
            } else if (moveCount == 0) {
                score = -Negamax(-beta, -alpha, depth - 1, ply + 1, ref outMove);
            } else {
                int reduction = move.CapturePieceType != 0 ? 0 : (moveCount * 3 + depth * 4) / 40;
                score = -Negamax(-alpha - 1, -alpha, depth - reduction - 1, ply + 1, ref outMove);
                if (score > alpha && reduction != 0) {
                    score = -Negamax(-alpha - 1, -alpha, depth - 1, ply + 1, ref outMove);
                }
                if (score > alpha && score < beta) {
                    score = -Negamax(-beta, -alpha, depth - 1, ply + 1, ref outMove);
                }
            }

            board.UndoMove(move);
            if (score > bestScore) {
                bestScore = score;
                bestMove = move;
            }
            if (score >= beta) {
                if (move.CapturePieceType == 0) {
                    int change = depth * depth;
                    for (int j = 0; j < moveCount; j++) {
                        if (moves[j].CapturePieceType == 0) {
                            HistoryValue(moves[j]) -= (short)(change + change * HistoryValue(moves[j]) / 4096);
                        }
                    }
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

        tt.bound = (byte)(bestScore >= beta ? 2 /* BOUND_LOWER */
            : raisedAlpha ? 1 /* BOUND_EXACT */
            : 3 /* BOUND_UPPER */);
        tt.depth = (byte)Math.Max(depth, 0);
        tt.hash = board.ZobristKey;
        tt.score = (short)bestScore;
        if (!tt_good || tt.bound != 3 /* BOUND_UPPER */)
            tt.moveRaw = bestMove.RawValue;

        outMove = bestMove;
        return bestScore;
    }

    ref short HistoryValue(Move move) {
        return ref history[board.IsWhiteToMove ? 1 : 0, (int)move.MovePieceType, (int)move.TargetSquare.Index];
    }
}

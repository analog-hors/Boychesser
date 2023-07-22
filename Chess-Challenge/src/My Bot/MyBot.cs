using ChessChallenge.API;
using System;

public class MyBot : IChessBot
{

    public long nodes = 0;
    public int maxSearchTime;

    public Move Think(Board board, Timer timer)
    {
        nodes = 0;
        maxSearchTime = timer.MillisecondsRemaining / 80;

        Move best = Move.NullMove;

        for (int depth = 1; depth <= 200; depth++) {
            //If score is of this value search has been aborted, DO NOT use result
            try {
                (int score, Move bestMove) = Negamax(board, -999999, 999999, depth, timer, depth);
                best = bestMove;
                //Use for debugging, commented out because it saves a LOT of tokens!!
                //Console.WriteLine("info depth " + depth + " score cp " + score);
            } catch (Exception) {
                break;
            }
        }

        return best;
    }

    public (int, Move) Negamax(Board board, int alpha, int beta, int depth, Timer timer, int searchingDepth)
    {
        //abort search
        if (timer.MillisecondsElapsedThisTurn >= maxSearchTime && searchingDepth > 1) {
            throw new Exception();
        }

        //node count
        nodes++;

        // check for game end
        if (board.IsInCheckmate())
        {
            return (-100000, Move.NullMove);
        }

        // static eval for qsearch
        int staticEval;
        if (depth <= 0)
        {
            PieceList[] pieceLists = board.GetAllPieceLists();
            staticEval = (pieceLists[0].Count - pieceLists[6].Count)
                + 3 * (pieceLists[1].Count + pieceLists[2].Count - pieceLists[7].Count - pieceLists[8].Count)
                + 5 * (pieceLists[3].Count - pieceLists[9].Count)
                + 9 * (pieceLists[4].Count - pieceLists[10].Count);
            staticEval = board.IsWhiteToMove ? staticEval : -staticEval;
            if (staticEval >= beta)
            {
                return (staticEval, Move.NullMove);
            }
            if (staticEval > alpha)
            {
                alpha = staticEval;
            }
        }

        Move[] moves = board.GetLegalMoves(depth <= 0);

        // sort moves MVV-LVA
        Array.Sort(moves, (m1, m2) => (m2.CapturePieceType - m1.CapturePieceType) * 8 + m1.MovePieceType - m2.MovePieceType);
        Move bestMove = Move.NullMove;
        foreach (Move move in moves)
        {
            board.MakeMove(move);
            int score = board.IsDraw() ? 0 : -Negamax(board, -beta, -alpha, depth - 1, timer, searchingDepth).Item1;
            board.UndoMove(move);

            if (score >= beta)
            {
                return (score, move);
            }
            if (score > alpha)
            {
                alpha = score;
                bestMove = move;
            }
        }

        return (alpha, bestMove);
    }
}
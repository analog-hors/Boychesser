using ChessChallenge.API;
using System;

public class MyBot : IChessBot
{

    public long nodes = 0;
    int maxSearchTime = -1;

    public Move Think(Board board, Timer timer)
    {
        nodes = 0;
        this.maxSearchTime = timer.MillisecondsRemaining / 80;

        Move best = Move.NullMove;

        for (int depth = 1; true; depth++) {
            //If score is of this value search has been aborted, DO NOT use result
            try {
                (int score, Move bestMove) = Negamax(board, -999999, 999999, depth, timer, depth);
                best = bestMove;
                //Use for debugging, commented out because it saevs a LOT of tokens!!
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
        if (timer.MillisecondsElapsedThisTurn >= this.maxSearchTime && searchingDepth > 1 && this.maxSearchTime > 0) {
            throw new Exception();
        }

        //node count
        nodes++;

        // check for game end
        if (board.IsDraw())
        {
            return (0, Move.NullMove);
        }
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
        Array.Sort(moves, (m1, m2) => ((m2.CapturePieceType - m2.MovePieceType).CompareTo(m1.CapturePieceType - m1.MovePieceType)));
        Move bestMove = Move.NullMove;
        foreach (Move move in moves)
        {
            board.MakeMove(move);
            (int score, Move nextMove) = Negamax(board, -beta, -alpha, depth - 1, timer, searchingDepth);
            board.UndoMove(move);

            if (-score >= beta)
            {
                return (-score, move);
            }
            if (-score > alpha)
            {
                alpha = -score;
                bestMove = move;
            }
        }

        return (alpha, bestMove);
    }
}

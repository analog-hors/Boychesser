﻿using ChessChallenge.API;
using System;

public class MyBot : IChessBot
{

    int nodes = 0;

    public Move Think(Board board, Timer timer)
    {
        this.nodes = 0;
        (int score, Move bestMove) = Negamax(board, -999999, 999999, 4);
        return bestMove;
    }

    public void BenchSearch(Board board) {
        this.nodes = 0;
        Negamax(board, -999999, 999999, 4);
    } 

    public int GetNodeCount() {
        return this.nodes;
    }

    (int, Move) Negamax(Board board, int alpha, int beta, int depth)
    {
        //node count
        this.nodes++;

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
            (int score, Move nextMove) = Negamax(board, -beta, -alpha, depth - 1);
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

﻿using ChessChallenge.API;
using System;
using static System.Math;
using static ChessChallenge.API.BitboardHelper;

public class MyBot : IChessBot {

    public long nodes = 0; // #DEBUG
    public int maxSearchTime, searchingDepth;

    public Timer timer;
    public Board board;

    Move nullMove, searchBestMove, rootBestMove;

    // Assuming the size of TtEntry is indeed 16 bytes, this table is precisely 256MiB.
    (
        ulong, // hash
        ushort, // moveRaw
        short, // score
        short, // depth 
        ushort // bound BOUND_EXACT=[1, 65535), BOUND_LOWER=65535, BOUND_UPPER=0
    )[] transpositionTable = new (ulong, ushort, short, short, ushort)[0x1000000];

    int[,,] history = new int[2, 7, 64];

    ulong[] packedData = {
        0x005f002e00000000, 0x010e00bf00d80099, 0x043b01dc01bb00ce, 0x0002000200060004,
        0xfffcfffd00020002, 0x00030003ffecfff9, 0xfff3fff4fffa0004, 0xfffd000bfff30000,
        0x0000000000000000, 0x30310b16342c1c05, 0x252616182d241d0c, 0x2129201e352b1509,
        0x313a2923443f220e, 0x5e693e387d6f2d15, 0xa1bc6452d5c52d41, 0x0000000000000000,
        0x6b62524e4e394a42, 0x79725d5a705a504e, 0x8f856b628066604e, 0x9c996d6b8a79695e,
        0xa59e757a957d6a6d, 0x96979e9486798e64, 0x9184888382685a58, 0x879f6d0b862e1802,
        0x45432f3049403135, 0x4648343645483e3d, 0x504e35394f513d3b, 0x4a523f344f4b3640,
        0x4e514640574f3b3d, 0x4c52554e5857523f, 0x5a58343a5c58332d, 0x6a6b120267630e1e,
        0x959b6d669e9a615e, 0x9d9d5e599b9c554c, 0xa5a85a58a6a35b52, 0xafb4615fb3b15759,
        0xb7bc7770c0ba6867, 0xb3bb9486b9bf8674, 0xbcbc9e95c5bf777b, 0xb8ba999abbb79895,
        0x887d959588a1958e, 0x9786959a8f9c999a, 0xb5b98e93ada79699, 0xe4d3858dd1bf8e98,
        0xf7f2898eecc08ea3, 0xfcfe9a97e4d7a8a2, 0xfff7929bfedb8a9d, 0xd1d8bab9e0e0aa9b,
        0x1d24252719003d33, 0x4d440a18311b2b2e, 0x645b020147311209, 0x74681312573e1200,
        0x7f7927226c50240b, 0x859132228c611c1c, 0x849231279c4d1026, 0x565a89835b01676e,
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
                Negamax(-32000, 32000, searchingDepth, 0);
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
            ttHit = tt.Item1 /* hash */ == board.ZobristKey,
            nonPv = alpha + 1 == beta,
            inQSearch = depth <= 0;
        int
            eval = 0x00000006,
            bestScore = -99999,
            oldAlpha = alpha,

            // search loop vars
            moveCount = 0, // quietsToCheckTable = [0, 5, 8, 14, 49]
            quietsToCheck = 0b_110001_001110_001000_000101_000000 >> depth * 6 & 0b111111,

            // static eval vars
            pieceType,

            // temp vars
            score = tt.Item3 /* score */,
            tmp = 0;
        if (ttHit) {
            if (tt.Item4 /* depth */ >= depth && tt.Item5 /* bound */ switch {
                65535 /* BOUND_LOWER */ => score >= beta,
                0 /* BOUND_UPPER */ => score <= alpha,
                _ /* BOUND_EXACT */ => nonPv || inQSearch,
            })
                return score;
        } else if (depth > 5)
            // Internal Iterative Reduction (IIR)
            depth--;

        if (ttHit && !inQSearch)
            eval = score;
        else {
            ulong pieces = board.AllPiecesBitboard;
            // use tmp as phase (initialized above)
            // tempo
            while (pieces != 0) {
                Square square = new(ClearAndGetIndexOfLSB(ref pieces));
                Piece piece = board.GetPiece(square);
                pieceType = (int)piece.PieceType;
                eval += (piece.IsWhite == board.IsWhiteToMove ? 1 : -1) * (
                    // material
                    EvalWeight(pieceType)
                        // psts
                        + (int)(
                            packedData[pieceType * 8 + square.Rank ^ (piece.IsWhite ? 0 : 0b111)]
                                >> (0x01455410 >> square.File * 4) * 8
                                & 0xFF00FF
                        )
                        // mobility
                        + EvalWeight(3 + pieceType) * GetNumberOfSetBits(
                            GetSliderAttacks((PieceType)Min(5, pieceType), square, board)
                        )
                        // own pawn on file
                        + EvalWeight(9 + pieceType) * GetNumberOfSetBits(
                            0x0101010101010101UL << square.File
                                & board.GetPieceBitboard(PieceType.Pawn, piece.IsWhite)
                        )
                );
                // phaseWeightTable = [X, 0, 1, 1, 2, 4, 0]
                tmp += 0x0421100 >> pieceType * 4 & 0xF;
            }
            // note: the correct way to extract EG eval is (eval + 0x8000) / 0x10000, but token count
            eval = ((short)eval * tmp + eval / 0x10000 * (24 - tmp)) / 24;
            // end tmp use
        }

        if (inQSearch)
            // stand pat in quiescence search
            alpha = Max(alpha, bestScore = eval);
        else if (nonPv && eval >= beta && board.TrySkipTurn()) {
            // Null Move Pruning (NMP)
            bestScore = depth <= 3 ? eval - 44 * depth : -Negamax(-beta, -alpha, (depth * 77 + beta - eval) / 120 - 1, nextPly);
            board.UndoSkipTurn();
        }
        if (bestScore >= beta)
            return bestScore;

        var moves = board.GetLegalMoves(inQSearch);
        var scores = new int[moves.Length];
        // use tmp as scoreIndex
        tmp = 0;
        foreach (Move move in moves)
            // sort capture moves by MVV-LVA, quiets by history, and hashmove first
            scores[tmp++] -= ttHit && move.RawValue == tt.Item2 /* moveRaw */ ? 100000
                : move.IsCapture ? (int)move.CapturePieceType * 4096 - (int)move.MovePieceType
                : HistoryValue(move);
        // end tmp use

        Array.Sort(scores, moves);
        Move bestMove = nullMove;
        foreach (Move move in moves) {
            // Delta pruning
            // deltas = [208, 382, 440, 640, 1340]
            // due to sharing of the top bit of each entry with the bottom bit of the next one
            // (expands the range of values for the queen) all deltas must be even (except pawn)
            if (inQSearch && eval + (0b1_0100111100_1010000000_0110111000_0101111110_0011010000_0000000000 >> (int)move.CapturePieceType * 10 & 0b1_11111_11111) <= alpha)
                break;

            board.MakeMove(move);
            int nextDepth = board.IsInCheck() ? depth : depth - 1;
            if (moveCount != 0) {
                // use tmp as reduction
                tmp = Max(
                    move.IsCapture || nextDepth >= depth ? 0
                    : (moveCount * 76 + depth * 103) / 1000 + Min(moveCount / 7, 1) + scores[moveCount] / 256,
                    0
                );
                score = -Negamax(~alpha, -alpha, nextDepth - tmp, nextPly);
                if (score > alpha && tmp != 0)
                    score = -Negamax(~alpha, -alpha, nextDepth, nextPly);
                // end tmp use
            }
            if (moveCount == 0 || score > alpha && score < beta)
                score = -Negamax(-beta, -alpha, nextDepth, nextPly);

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

            // Pruning techniques that break the move loop
            if (nonPv && depth <= 4 && !move.IsCapture && (
                // LMP
                quietsToCheck-- == 1 ||
                // History Pruning
                eval <= alpha && scores[moveCount] > 64 * depth ||
                // Futility Pruning
                eval + 271 * depth < alpha
            ))
                break;

            moveCount++;
        }

        // use tmp as bound
        tmp = bestScore >= beta ? 65535 /* BOUND_LOWER */
            : alpha - oldAlpha /* BOUND_UPPER if alpha == oldAlpha else BOUND_EXACT */;
        tt = (
            board.ZobristKey,
            tmp /* bound */ != 0 /* BOUND_UPPER */
                ? bestMove.RawValue
                : tt.Item2 /* moveRaw */,
            (short)bestScore,
            (short)Max(depth, 0),
            (ushort)tmp
        );
        // end tmp use
        
        searchBestMove = bestMove;
        return bestScore;
    }

    ref int HistoryValue(Move move) => ref history[
        board.IsWhiteToMove ? 1 : 0,
        (int)move.MovePieceType,
        move.TargetSquare.Index
    ];
}

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

    int[] packedPstBases = {
        0x0052001B, 0x00FB00A5, 0x011700B4, 0x01A600CD, 0x0448019B, 0x00000000
    };
    ulong[] packedPstDeltas = {    
        0x0000000000000000,
        0x2C2B0E1C30272101,
        0x1F1D191B271E2508,
        0x182025202C241E08,
        0x242D2E273938270D,
        0x4E5A453C70653215,
        0x91AD6853C7B62F48,
        0x0000000000000000,
        0x473F413D29163933,
        0x5C52474751373C3B,
        0x6F65554F5E444E3E,
        0x7B7859596858594F,
        0x817A676B745C5B5F,
        0x7072938763568359,
        0x6A607F795E455154,
        0x6178650661050E00,
        0x5B5440425D4F4243,
        0x69694E52645C5550,
        0x7A7756566F675951,
        0x7E7C625972625557,
        0x7F79706A7B655A59,
        0x727A8077756B765E,
        0x77735B6077675851,
        0x7F7F2E217A752C35,
        0xC7C97068C9BD6159,
        0xCAC8605BC4C15748,
        0xD2D15C5ACDC75F51,
        0xDADC6A66D9D5605D,
        0xDFE2857CE5DF746E,
        0xD9DFA594DDE3937D,
        0xE2E0AFA5E7E0878C,
        0xDEDEAEACDED9A7A5,
        0x8986DAD790AED8CE,
        0xA18DDDE598A1E1DD,
        0xBBBEDCE0B0A8E3E2,
        0xE8D7DADDD1BEDDE2,
        0xF9F1E1E5ECBFDFF0,
        0xFBFEF3EDE1D5FBEE,
        0xFEF3E7EFFCD0D9EF,
        0xD9DEFFFFDCDCF6E4,
        0x08173A3D15015859,
        0x3E351125281C3A49,
        0x514801023C311B17,
        0x5C560C0E4D3F1506,
        0x68671E1D62502613,
        0x7482281B83631F27,
        0x7C8B272297521738,
        0x575C81835E04728A,
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
        bool
            ttHit = tt.hash == board.ZobristKey,
            nonPv = alpha + 1 == beta;
        int
            bestScore = -99999,
            oldAlpha = alpha,

            // search loop vars
            moveCount = 0, // quietsToCheckTable = [0, 7, 8, 17, 49]
            quietsToCheck = 0b_110001_010001_001000_000111_000000 >> depth * 6 & 0b111111,

            // static eval vars
            pieceType,

            // temp vars
            score = tt.score,
            tmp = tt.bound;

        // use tmp as tt.bound
        if (ttHit && tt.depth >= depth && (
            tmp == 1 /* BOUND_EXACT */ && (nonPv || depth <= 0) ||
            tmp == 2 /* BOUND_LOWER */ && score >= beta ||
            tmp == 3 /* BOUND_UPPER */ && score <= alpha
        ))
            return score;
        // end tmp use

        // Null Move Pruning (NMP)
        if (nonPv && depth >= 1 && board.TrySkipTurn()) {
            score = -Negamax(-beta, -alpha, depth - 3, nextPly);
            board.UndoSkipTurn();
            if (score >= beta)
                return score;
        }


        // static eval for qsearch
        if (depth <= 0) {
            // use tmp as phase
            bestScore = tmp = 0;
            ulong pieces = board.AllPiecesBitboard;
            while (pieces != 0) {
                Square square = new(BitboardHelper.ClearAndGetIndexOfLSB(ref pieces));
                Piece piece = board.GetPiece(square);
                pieceType = (int)piece.PieceType - 1;
                bestScore += (piece.IsWhite == board.IsWhiteToMove ? 1 : -1) * (
                    packedPstBases[pieceType] + (int)(
                        packedPstDeltas[square.Rank ^ (piece.IsWhite ? 0 : 0b111)]
                            >> (0x01455410 >> square.File * 4) * 8
                            & 0xFF00FF
                    )
                );
                // phase weight expression
                // maps 0 1 2 3 4 5 to 0 1 1 2 4 0
                tmp += (pieceType + 2 ^ 2) % 5;
            }

            alpha = Max(
                alpha,
                bestScore = ((short)bestScore * tmp + (bestScore + 0x8000) / 0x10000 * (24 - tmp)) / 24
            );
            // end tmp use
            
            if (bestScore >= beta)
                return bestScore;
        }

        var moves = board.GetLegalMoves(depth <= 0);
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
            if (nonPv && depth <= 4 && !move.IsCapture && quietsToCheck-- == 0)
                break;

            board.MakeMove(move);
            int nextDepth = board.IsInCheck() ? depth : depth - 1;
            if (board.IsDraw())
                score = 0;
            else if (moveCount == 0)
                score = -Negamax(-beta, -alpha, nextDepth, nextPly);
            else {
                // use tmp as reduction
                tmp = move.IsCapture || board.IsInCheck() ? 0
                    : (moveCount * 3 + depth * 4) / 40 + Convert.ToInt32(moveCount > 4);
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
                            HistoryValue(malusMove) -= (short)(tmp + tmp * HistoryValue(malusMove) / 4096);
                    HistoryValue(move) += (short)(tmp - tmp * HistoryValue(move) / 4096);
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

    ref short HistoryValue(Move move) => ref history[
        board.IsWhiteToMove ? 1 : 0,
        (int)move.MovePieceType,
        (int)move.TargetSquare.Index
    ];
}

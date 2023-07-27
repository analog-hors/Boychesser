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

    ulong[] packedEvalWeights = {
        0x0000000000000000, 0x0000000000000000, 0x0079004000740022, 0x0074002E00740039, 
        0x00730041006C0027, 0x006900390069003A, 0x0079003900720024, 0x00660043006C003F, 
        0x0086004200850027, 0x00730048007C0040, 0x00BD004B00B2002D, 0x00A0005A00AB0051, 
        0x0111004701020059, 0x00E2007900FB0069, 0x0003000300060004, 0xFFFCFFFD000A0001, 
        0x011300E300FD00D8, 0x012E00E9012500E5, 0x013500E4011F00E3, 0x013E00F2013600EE, 
        0x014300F4012A00E5, 0x01530100014900F8, 0x014F00FF013F00F4, 0x01600101015D0101, 
        0x0159010001430103, 0x0168010A0162010E, 0x014B0123013E00FA, 0x015B0132015C0127, 
        0x014700F1012E00F2, 0x0156011C01490119, 0x014A00B200FA009C, 0x014C0104016200A6, 
        0x013800EB013100ED, 0x013400E9013100EA, 0x013500F5013900F5, 0x013500EB013700EE, 
        0x013E00F5014000F4, 0x013E00ED013C00F1, 0x013E00EF013B00F9, 0x013900F6013F00ED, 
        0x014600F4013F00F6, 0x013C00FF013F00FA, 0x0146010C014600F9, 0x013B010C01410106, 
        0x014A00EC014600EB, 0x014900EC014600F4, 0x015600C9015100DB, 0x015A00CD015A00BE, 
        0x0225011D021F0118, 0x021E012A02230121, 0x0224011102260106, 0x0227011B02260115, 
        0x022E0114022C010A, 0x022E0116022F0112, 0x023A011402380113, 0x0238011E023C011B, 
        0x0247012502410122, 0x024001330243012D, 0x023F01430246012F, 0x023C015002420141, 
        0x024B013402450139, 0x0245015A02440151, 0x023F0154023B0152, 0x023F0153023F0155, 
        0x03F2022E04150225, 0x03F4022F03E4022E, 0x03EE023304090233, 0x03F0022E03E10233, 
        0x04070230040F0232, 0x03FC02290404022D, 0x0423022804240231, 0x041A022304180228, 
        0x043D02290426023D, 0x042C02280433022B, 0x043902430443023A, 0x0440023604460231, 
        0x045902240447023A, 0x045B0228044A0237, 0x0436024E043D0240, 0x0423025A042A025D, 
        0xFFC70015FFAE000A, 0xFFCAFFFEFFD30000, 0xFFDE0002FFC70004, 0xFFF9FFE2FFF0FFF0, 
        0xFFF3FFEAFFDCFFE0, 0x0010FFDA0007FFDA, 0x0004FFEBFFECFFD4, 0x0021FFEC0014FFEA, 
        0x0019FFFCFFFEFFE2, 0x002E00010026FFFB, 0x0039FFF4000FFFF3, 0x00340008003FFFF9, 
        0x0048FFECFFFDFFFB, 0x0033000900400000, 0x000C0041FFB8003B, 0x0007005F000B0059,
    };

    int EvalWeight(int item) => (int)(packedEvalWeights[item / 2] >> item % 2 * 32);

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
            sq,
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
            score = -Negamax(-beta, -alpha, depth * 2 / 3 - 2, nextPly);
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
                Square square = new(sq = BitboardHelper.ClearAndGetIndexOfLSB(ref pieces));
                Piece piece = board.GetPiece(square);
                sq = sq >> 1 & 0b11100 | sq & 0b11 ^ square.File / 4 * 0b11;
                pieceType = (int)piece.PieceType - 1;
                bestScore += (piece.IsWhite == board.IsWhiteToMove ? 1 : -1) * (
                    EvalWeight((piece.IsWhite ? sq : sq ^ 0b11100) + pieceType * 32) +
                    EvalWeight(26 + pieceType) * BitboardHelper.GetNumberOfSetBits(
                        BitboardHelper.GetSliderAttacks((PieceType)Min(5, pieceType+1), square, board)
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

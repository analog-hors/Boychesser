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
        0x00720042006B0028, 0x006900390068003A, 0x0079003900720025, 0x00660043006C003F,
        0x0086004300850028, 0x00730049007C0041, 0x00BD004C00B2002E, 0x00A0005A00AA0052,
        0x011100480101005A, 0x00E1007A00FA0069, 0x0003000300060004, 0xFFFCFFFD000A0001,
        0x011300E200FE00D8, 0x012E00EA012500E5, 0x013500E5011E00E3, 0x013E00F3013600EF,
        0x014300F5012A00E5, 0x01520100014900F9, 0x014E0100013F00F4, 0x01600102015D0102,
        0x0159010101420104, 0x0168010B0162010F, 0x014A0125013E00FB, 0x015B0133015C0128,
        0x014700F2012D00F3, 0x0155011D0149011A, 0x014900B300FA009D, 0x014B0105016200A7,
        0x013800EB013100ED, 0x013500E9013200E9, 0x013500F5013900F5, 0x013500EC013700EE,
        0x013F00F5014000F4, 0x013E00ED013C00F2, 0x013F00EF013B00F9, 0x013A00F7014000ED,
        0x014700F4013F00F7, 0x013C0100013F00FA, 0x0147010D014700F9, 0x013C010C01410107,
        0x014A00ED014700EB, 0x014A00ED014700F5, 0x015600CA015200DC, 0x015A00CD015B00BE,
        0x0226011D021F0118, 0x021E012B02230122, 0x0224011202260106, 0x0227011C02260116,
        0x022E0115022C010B, 0x022F011702300113, 0x023A011502380114, 0x0238011F023C011C,
        0x0247012602420123, 0x024001340244012E, 0x0240014502460130, 0x023C015202430143,
        0x024B01350245013B, 0x0245015C02440153, 0x023F0155023B0153, 0x023F015402400156,
        0x03F4022F04160225, 0x03F5022E03E5022E, 0x03EE0233040A0233, 0x03F1022F03E20234,
        0x0407023004100232, 0x03FD02290405022D, 0x0424022904250232, 0x041B022404190228,
        0x043E022A0428023D, 0x042D02280434022B, 0x043B02440444023B, 0x0441023704470232,
        0x045902250448023B, 0x045C0229044B0238, 0x0436024F043D0240, 0x0423025B042A025D,
        0xFFC70015FFAE000A, 0xFFCBFFFEFFD30000, 0xFFDE0002FFC70005, 0xFFF9FFE2FFF0FFF0,
        0xFFF3FFEAFFDCFFE0, 0x0010FFDA0007FFDA, 0x0004FFEBFFECFFD4, 0x0021FFEC0015FFEA,
        0x0019FFFCFFFEFFE2, 0x002E00010026FFFB, 0x0039FFF4000FFFF3, 0x00340008003FFFF9,
        0x0048FFECFFFDFFFB, 0x0033000900400000, 0x000C0041FFB8003C, 0x0007005F000B0059,
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
            score = -Negamax(-beta, -alpha, depth - 3, nextPly);
            board.UndoSkipTurn();
            if (score >= beta)
                return score;
        }


        // static eval for qsearch
        if (depth <= 0) {
            // use tmp as phase
            bestScore = 5;
            tmp = 0;
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

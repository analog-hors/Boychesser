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
        0x0000000000000000, 0x0000000000000000, 0x007A004600730029, 0x007400330075003D,
        0x00720047006B002F, 0x0069003D0069003F, 0x0079003F0072002B, 0x00660047006C0044,
        0x008600480085002F, 0x0073004C007C0045, 0x00BD005100B20034, 0x00A0005D00AA0056,
        0x0112004D01020061, 0x00E1007E00FA006F, 0x0003000300060004, 0xFFFCFFFD000A0001,
        0x011400E800FE00DE, 0x012F00EE012600EA, 0x013500EA011F00E9, 0x013F00F7013700F3,
        0x014400FA012A00EC, 0x01530104014900FD, 0x014F0103013F00FB, 0x01610105015E0106,
        0x015901060143010A, 0x0169010E01630113, 0x014B012A013F0100, 0x015B0137015C012E,
        0x014700F9012E00FB, 0x0155012201490120, 0x014A00B900FA00A5, 0x014C0108016200AB,
        0x013900F0013200F3, 0x013600ED013400ED, 0x013500F9013900FB, 0x013600EF013900F2,
        0x014000F9014100FA, 0x014000F0013D00F5, 0x014000F2013C00FE, 0x013B00F9014100EF,
        0x014800F8014000FB, 0x013E0101014100FC, 0x01480110014800FC, 0x013D010F0142010B,
        0x014B00F3014900EF, 0x014A00EF014700F8, 0x015700CE015300E2, 0x015B00D0015C00C0,
        0x022701220221011F, 0x021F012F02230126, 0x022501170227010D, 0x022801200227011A,
        0x022F011A022D0111, 0x0230011A02300117, 0x023B011A0239011A, 0x02390123023D0120,
        0x0248012B0242012A, 0x0241013802440132, 0x0240014B02470138, 0x023D015502430147,
        0x024B013C02450144, 0x0245016002440158, 0x0240015B023B015B, 0x023F01580240015B,
        0x03F70231041A0228, 0x03F9022E03E8022F, 0x03F20235040E0236, 0x03F6022E03E50234,
        0x0409023204120235, 0x040202290409022D, 0x0427022804240235, 0x04210223041C0228,
        0x0441022B0429023F, 0x043202270438022B, 0x043D02450446023D, 0x0445023604480234,
        0x045A022A0449023F, 0x045E022A044D023A, 0x04390250043F0244, 0x0427025A042D025E,
        0xFFCA001FFFAD0025, 0xFFD4FFF3FFD9FFFD, 0xFFE30009FFC8001C, 0x0001FFD8FFF7FFEA,
        0xFFF8FFEFFFE0FFF2, 0x0017FFD1000DFFD5, 0x0009FFEEFFF1FFE1, 0x0027FFE5001AFFE6,
        0x001FFFFE0003FFEC, 0x0032FFFC002CFFF8, 0x003FFFF60015FFFB, 0x003A00060045FFF7,
        0x004EFFEE00040003, 0x0038000B00460001, 0x00120045FFBD0044, 0x000C00630011005D
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
                        BitboardHelper.GetSliderAttacks((PieceType)Min(5, pieceType + 1), square, board)
                    ) - 2 * Abs(square.File - board.GetKingSquare(piece.IsWhite).File)
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

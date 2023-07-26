using ChessChallenge.API;
using System;
using static System.Math;
using static ChessChallenge.API.BitboardHelper;

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
        0x0000000000000000, 0x0000000000000000, 0x007D003C0077001C, 0x007B0029007A0038, 
        0x00750040006D0023, 0x006D0034006B0035, 0x007B003800730022, 0x00660040006D003B, 
        0x0087004300850027, 0x00730049007B0042, 0x00BB004E00B00031, 0x009B006100A60058, 
        0x010F004B00FF0065, 0x00DB008500F6006F, 0x0000000000000000, 0x0000000000000000, 
        0x011100E000FE00D9, 0x012B00E8012700E3, 0x013700E4011F00E2, 0x014300ED013900EE, 
        0x014200F5012C00E6, 0x015500FC014B00F6, 0x014D00FF013E00F6, 0x01600100015E0101, 
        0x0159010201420105, 0x0167010F01600112, 0x01480129013D0102, 0x015501390156012D, 
        0x014300F7012C00FA, 0x014F012601460122, 0x014800B800FA0099, 0x0144010C015B00AA, 
        0x015D00F9014E00FB, 0x015C00F8015400F8, 0x0164010C015B0107, 0x0168010501680108, 
        0x016E011001660106, 0x0178010C0175010E, 0x0171010E0162010F, 0x017C0119017B0110, 
        0x017B011001650111, 0x017E012501770120, 0x0173012B01690115, 0x017101360177012D, 
        0x0175010F01630108, 0x0176011201730116, 0x017800E3017700ED, 0x017C00E7017D00D8, 
        0x024A0134023E012C, 0x024601440249013A, 0x024501290243011B, 0x024B01330248012F, 
        0x024C013202480124, 0x0252012F0251012C, 0x025901340254012F, 0x0259013D025B0137, 
        0x02630148025E0141, 0x025E015802620150, 0x025D016602620150, 0x02590179025F0168, 
        0x0267015B0260015F, 0x0261018102600178, 0x025C017902590177, 0x025D017F025C017D, 
        0x0484025B04A00251, 0x047D025D0479025B, 0x048A02640492025F, 0x0494026104830269, 
        0x04A40267049B0264, 0x04AF026004B20262, 0x04C4026204B00265, 0x04DB025D04CC0261, 
        0x04DF026204B30274, 0x04EF026604E50268, 0x04D4027E04C50270, 0x04EF027704F10270, 
        0x04EE025C04C20272, 0x04F9026804E70274, 0x04CA027D04CD0269, 0x04BA029704BC0296, 
        0xFFCC0020FFBA0021, 0xFFC00002FFCF0005, 0xFFE00003FFD40012, 0xFFF4FFDAFFECFFEE, 
        0xFFF3FFE4FFE6FFE0, 0x0006FFC9FFFEFFCD, 0x0003FFDFFFF5FFD0, 0x0011FFD5000CFFD9, 
        0x0019FFF00006FFDA, 0x001DFFE9001EFFE8, 0x0038FFE70018FFED, 0x002AFFF30037FFE5, 
        0x0049FFE0000CFFFE, 0x0031FFF0003EFFEA, 0x00180038FFC7003D, 0x0011004000140042,
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
        bool tt_good = tt.hash == board.ZobristKey;
        bool nonPv = alpha + 1 == beta;

        if (tt_good && tt.depth >= depth && (
            tt.bound == 1 /* BOUND_EXACT */ && (nonPv || depth <= 0) ||
            tt.bound == 2 /* BOUND_LOWER */ && tt.score >= beta ||
            tt.bound == 3 /* BOUND_UPPER */ && tt.score <= alpha
        ))
            return tt.score;

        // Null Move Pruning (NMP)
        if (nonPv && depth >= 1 && board.TrySkipTurn()) {
            var result = -Negamax(-beta, 1 - beta, depth - 3, nextPly);
            board.UndoSkipTurn();
            if (result >= beta)
                return result;
        }

        int bestScore = -99999, oldAlpha = alpha;

        // static eval for qsearch
        if (depth <= 0) {
            int phase = 0, staticEval = 0;
            ulong pieces = board.AllPiecesBitboard;
            while (pieces != 0) {
                int sq = BitboardHelper.ClearAndGetIndexOfLSB(ref pieces);
                Square square = new(sq);
                Piece piece = board.GetPiece(square);
                sq = sq >> 1 & 0b11100 | (square.File >= 4 ? sq ^ 7 : sq) & 0b11;
                int pieceType = (int)piece.PieceType - 1;
                staticEval += (piece.IsWhite == board.IsWhiteToMove ? 1 : -1) * (int)(packedEvalWeights[
                    (piece.IsWhite ? sq : sq ^ 0b11100) / 2 + pieceType * 16
                ] >> sq % 2 * 32);
                phase += (pieceType + 2 ^ 2) % 5;
            }
            staticEval = ((short)staticEval * phase + (staticEval + 0x8000) / 0x10000 * (24 - phase)) / 24;
            if (staticEval >= beta)
                return staticEval;

            alpha = Max(alpha, staticEval);
            bestScore = staticEval;
        }

        var moves = board.GetLegalMoves(depth <= 0);
        var scores = new int[moves.Length];
        int scoreIndex = 0;
        foreach (Move move in moves)
            // sort capture moves by MVV-LVA, quiets by history, and hashmove first
            scores[scoreIndex++] -= tt_good && move.RawValue == tt.moveRaw ? 10000
                : move.IsCapture ? (int)move.CapturePieceType * 8 - (int)move.MovePieceType + 5000
                : HistoryValue(move);

        Array.Sort(scores, moves);
        Move bestMove = nullMove;
        // quietsToCheckTable = [0, 7, 8, 17, 49]
        int moveCount = 0, quietsToCheck = 0b_110001_010001_001000_000111_000000 >> depth * 6 & 0b111111, score;
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
                int reduction = move.IsCapture || board.IsInCheck() ? 0
                    : (moveCount * 3 + depth * 4) / 40 + Convert.ToInt32(moveCount > 4);
                score = -Negamax(-alpha - 1, -alpha, nextDepth - reduction, nextPly);
                if (score > alpha && reduction != 0)
                    score = -Negamax(-alpha - 1, -alpha, nextDepth, nextPly);
                if (score > alpha && score < beta)
                    score = -Negamax(-beta, -alpha, nextDepth, nextPly);
            }

            board.UndoMove(move);

            if (score > bestScore) {
                bestScore = score;
                bestMove = move;
            }
            if (score >= beta) {
                if (!move.IsCapture) {
                    int change = depth * depth;
                    foreach (Move malusMove in moves.AsSpan(0, moveCount))
                        if (!malusMove.IsCapture)
                            HistoryValue(malusMove) -= (short)(change + change * HistoryValue(malusMove) / 4096);
                    HistoryValue(move) += (short)(change - change * HistoryValue(move) / 4096);
                }
                break;
            }
            alpha = Max(alpha, score);
            moveCount++;
        }

        tt.bound = (short)(bestScore >= beta ? 2 /* BOUND_LOWER */
            : alpha > oldAlpha ? 1 /* BOUND_EXACT */
            : 3 /* BOUND_UPPER */);
        tt.depth = (short)Max(depth, 0);
        tt.hash = board.ZobristKey;
        tt.score = (short)bestScore;
        if (!tt_good || tt.bound != 3 /* BOUND_UPPER */)
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

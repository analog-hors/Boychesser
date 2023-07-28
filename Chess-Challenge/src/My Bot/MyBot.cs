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
        0x004F0021004F0021, 0x004F0021004F0021, 0x007D004100770023, 0x0078002F00780039,
        0x00770042006F0029, 0x006D003A006C003B, 0x007E003A00760025, 0x006A004400700040,
        0x008B0044008A0028, 0x0077004A00800042, 0x00C4004D00B8002E, 0x00A5005C00B00053,
        0x011B0048010B005A, 0x00E9007B0103006A, 0x0002000300060004, 0xFFFCFFFD00020002,
        0x012600E4011000D9, 0x014100EB013800E7, 0x014800E7013100E5, 0x015100F5014900F1,
        0x015700F7013E00E6, 0x01660102015C00FA, 0x01620102015300F6, 0x0174010401710103,
        0x016D010301560106, 0x017D010D01760111, 0x015F0127015100FD, 0x016F01360170012C,
        0x015A00F4014000F5, 0x01690120015C011D, 0x015E00B20108009C, 0x015F0106017600A6,
        0x015000EC014800EF, 0x014C00EA014900EB, 0x014D00F7015000F7, 0x014D00ED014F00EF,
        0x015700F6015800F6, 0x015700EF015500F3, 0x015700F0015300FA, 0x015400F8015A00EE,
        0x016000F5015700F8, 0x01560101015900FB, 0x0160010E015F00FB, 0x0155010F015B0108,
        0x016300EF015F00EC, 0x016200EE015F00F6, 0x016E00CB016A00DC, 0x017200CD017300BE,
        0x024C011E02450119, 0x0245012C024A0123, 0x024B0113024C0107, 0x024E011C024D0116,
        0x025501160253010C, 0x0256011702570113, 0x02610115025F0114, 0x025F01200263011D,
        0x026E012702680124, 0x02670136026A012F, 0x02660146026D0131, 0x0263015402690145,
        0x02720136026C013C, 0x026C015E026B0155, 0x0266015802620157, 0x026601580266015A,
        0x04B1026704CD025E, 0x04B1026704A60266, 0x04B9026A04C7026B, 0x04C0026504B0026A,
        0x04D6026604D1026A, 0x04DC025E04E00262, 0x04F9025E04E80268, 0x0509025604FA025C,
        0x0513025F04EA0274, 0x051C025B05170260, 0x050A027A05000273, 0x0522026C05250267,
        0x0525025C05000273, 0x05250264051D026D, 0x0504027E05010272, 0x04F9028A04FE028B,
        0x001A003F00010034, 0x001E00270026002A, 0x0032002B001A002E, 0x004E000B00440019,
        0x0049001200310008, 0x00660002005D0001, 0x00590012003F0000, 0x00770012006A0011,
        0x006F00230053000A, 0x00830026007C0021, 0x008F001B0064001C, 0x008A002E0096001E,
        0x00A0000F00510025, 0x0089002D00960023, 0x005F00650005006D, 0x005A0086005E0081,
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
            tmp = 0;

        if (ttHit && tt.depth >= depth && tt.bound switch {
            1 /* BOUND_EXACT */ => nonPv || depth <= 0,
            2 /* BOUND_LOWER */ => score >= beta,
            3 /* BOUND_UPPER */ => score <= alpha,
        })
            return score;

        // use tmp as phase (initialized above)
        score = 6;
        ulong pieces = board.AllPiecesBitboard;
        while (pieces != 0) {
            Square square = new(sq = BitboardHelper.ClearAndGetIndexOfLSB(ref pieces));
            Piece piece = board.GetPiece(square);
            sq = sq >> 1 & 0b11100 | sq & 0b11 ^ square.File / 4 * 0b11;
            pieceType = (int)piece.PieceType - 1;
            score += (piece.IsWhite == board.IsWhiteToMove ? 1 : -1) * (
                EvalWeight((piece.IsWhite ? sq : sq ^ 0b11100) + pieceType * 32) +
                EvalWeight(26 + pieceType) * BitboardHelper.GetNumberOfSetBits(
                    BitboardHelper.GetSliderAttacks((PieceType)Min(5, pieceType+1), square, board)
                )
            );
            // phase weight expression
            // maps 0 1 2 3 4 5 to 0 1 1 2 4 0
            tmp += (pieceType + 2 ^ 2) % 5;
        }
        score = ((short)score * tmp + (score + 0x8000) / 0x10000 * (24 - tmp)) / 24;
        // end tmp use

        if (depth <= 0) {
            // stand pat in quiescence search
            alpha = Max(alpha, bestScore = score);
            if (bestScore >= beta)
                return bestScore;
        } else if (nonPv && board.TrySkipTurn()) {
            // Null Move Pruning (NMP)
            score = depth < 4 ? score - 75 * depth : -Negamax(-beta, -alpha, depth - 3, nextPly);
            board.UndoSkipTurn();
            if (score >= beta)
                return score;
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

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
        0x0000000000000000, 0x0000000000000000, 0x007700550070003C, 0x0073003C00720048,
        0x006F005600670041, 0x006800460067004B, 0x0076004E006E003E, 0x00650050006A004F,
        0x0083005700810042, 0x00720055007A0051, 0x00BB006000AE0049, 0x009F006700A80064,
        0x0110005B00FE0076, 0x00E0008A00F8007E, 0x0003000300060004, 0xFFFCFFFD000A0001,
        0x011500EA00FF00E0, 0x013000F1012600ED, 0x013600ED012000EB, 0x014000FA013700F7,
        0x014500FD012C00EE, 0x01540107014A0100, 0x01500106014000FD, 0x01610109015E0109,
        0x015A01090144010C, 0x0169011201630117, 0x014C012D01400104, 0x015B013B015C0132,
        0x014800FC012F00FD, 0x01550127014A0122, 0x014B00BA00FC00A6, 0x014C010A016300AD,
        0x013C00EC013400EF, 0x013700ED013400EC, 0x013700F8013B00F7, 0x013700EF013A00F1,
        0x014100F8014200F6, 0x014100F0013E00F4, 0x014100F1013D00FB, 0x013C00F9014200EF,
        0x014900F6014200F7, 0x013E0101014200FC, 0x014A010D014900FA, 0x013D010F01430109,
        0x014D00EF014A00EC, 0x014B00EF014800F8, 0x015800CB015400DE, 0x015C00CD015D00BE,
        0x022801160222010F, 0x0222012802250121, 0x0226010F022900FE, 0x022A011C02280117,
        0x02310113022F0105, 0x0231011602320113, 0x023D0111023B010C, 0x023B011E023E011B,
        0x024A01200245011B, 0x024201330246012C, 0x0243013E02490128, 0x023F014F02450140,
        0x024E013002480133, 0x0247015B02460150, 0x0243014E023F0149, 0x0241015102420152,
        0x03F2022D040F0223, 0x03F7023003E8022F, 0x03F1023204040231, 0x03F5023003E70234,
        0x040B0231040D0232, 0x0403022B040B022E, 0x0427022904210231, 0x04230225041F0229,
        0x0440022A0425023D, 0x043402290438022C, 0x043B0244043F023B, 0x0443023904470235,
        0x045502290441023D, 0x045D022B044B023B, 0x0435024E0438023F, 0x04270259042D025B,
        0xFFCA001DFFAA002C, 0xFFD8FFECFFDBFFF8, 0xFFE1000BFFC40024, 0x0003FFD3FFF7FFE9,
        0xFFF7FFF2FFDDFFF7, 0x0018FFCF000DFFD5, 0x0009FFEFFFEFFFE5, 0x0027FFE6001AFFE7,
        0x001FFFFE0003FFED, 0x0032FFFD002CFFF9, 0x003FFFF50015FFFB, 0x003900090045FFF9,
        0x004FFFEF00040004, 0x0037001400460006, 0x0011004EFFBC004E, 0x000A0072000F0069,
        0xFFFFFFFE0000FFF9, 0xFFFF000400000000, 0x0000000000040000,
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
            bestScore = 6;
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
                    ) + EvalWeight(192 + pieceType) * Abs(square.File - board.GetKingSquare(piece.IsWhite).File)
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

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
        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 
        0x007B003400790019, 0x007900280077002E, 0x007600410076002B, 0x00710020007A0045, 
        0x00740037006E0021, 0x006A003100660034, 0x006B0037006A0036, 0x00690026006F004A, 
        0x0078003500740021, 0x0062003F00690039, 0x006A003E00650042, 0x006D00240077003D, 
        0x0086004100870028, 0x00710047007B003D, 0x007500490070004A, 0x007D002600810045, 
        0x00B9005000B30031, 0x0097006200A80053, 0x009B005E00980060, 0x00A4003200B5004C, 
        0x0103007400F70081, 0x00D4008B00EF007C, 0x00F2005E00D7007D, 0x00F9004B010A0025, 
        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 
        0x010100DF00F500DB, 0x011F00E6011500E6, 0x011C00E1011E00E8, 0x00EF00D8010A00E0, 
        0x012D00E0011300DC, 0x013600EC012D00EB, 0x012A00F3013500EE, 0x011100E4012600E7, 
        0x013400F1011C00E3, 0x014800FC013D00F5, 0x013C00F7014700FA, 0x012100E8013500F7, 
        0x013E00FB013000F2, 0x015100FE015000FD, 0x014F010301520102, 0x013000FB013F0102, 
        0x014800FE013300FD, 0x015601120151010E, 0x01530116015B010D, 0x01370107014E0106, 
        0x013D011E012F00FD, 0x014C012F01490121, 0x0145013D01410145, 0x01310105013C012B, 
        0x013500F6012000F0, 0x0140012D013B0116, 0x013301330141011F, 0x011E0102013500F6, 
        0x013C00AE00F00089, 0x013C00F4014800C8, 0x0150008E01300121, 0x00F200B8013700C5, 
        0x014E00FE014300FB, 0x014D00F9013E00FA, 0x014D00F7014E00F6, 0x013C00FC014D00F5, 
        0x0154010D0150010C, 0x015B0101015D010A, 0x01560108015A0106, 0x014A01020155010C, 
        0x0162011201570106, 0x0169010F0169010D, 0x0163010E0168010A, 0x01570106015D010F, 
        0x0163010C0155010E, 0x016D0119016C0112, 0x016A010F016C0119, 0x0151010E0162010D, 
        0x016C010D01560108, 0x016E01270166011D, 0x016A012201700123, 0x015A010F016C0113, 
        0x01640122015E0104, 0x01610134016A011F, 0x0165013F01630137, 0x0159011D0168012D, 
        0x0168010901540106, 0x016A010E01640112, 0x0163011B01640115, 0x0155010B01620118, 
        0x016900DF016D00E4, 0x016E00D8016A00EE, 0x017200C0016B00F6, 0x016100F7016900E6, 
        0x02340133022C012B, 0x023301400233013C, 0x022F0139022E0144, 0x021F0132022F0139, 
        0x02310128022E011E, 0x0235013202350130, 0x022C013202330133, 0x022A011A0228012F, 
        0x023A012D02330125, 0x023D012C023D012B, 0x0237012E02390131, 0x022C012A022F0138, 
        0x0246012F0241012B, 0x0243013A02470135, 0x024001390241013D, 0x02380135023B013A, 
        0x02510141024A013B, 0x02490155024F014A, 0x0246015502450159, 0x0242014702440151, 
        0x024B015D024F014A, 0x02460170024D015E, 0x02410176023E017F, 0x0245015902400173, 
        0x02530159024C015A, 0x024A0183024E0172, 0x0243017C0249017C, 0x02430168024B015F, 
        0x0246017602430172, 0x0246017E0245017C, 0x0245017B0245017D, 0x0240017C0242017B, 
        0x046B0228047F0221, 0x045C022B045C022A, 0x044D0224045A0229, 0x0470021E044C0229, 
        0x0478022E0471022B, 0x0473022F046B0233, 0x045B0236046E0231, 0x046D022D04510237, 
        0x04850230047E022D, 0x048C022E0496022E, 0x048C0232048E022E, 0x0473023504820238, 
        0x04A3022B0485022F, 0x04BE022A04A8022D, 0x04AB023204B00230, 0x049A023604A10232, 
        0x04B4022A04920234, 0x04CA023104C10230, 0x04C6023904CF0237, 0x04A2023F04C60234, 
        0x04B5023C049B0232, 0x04CB024304C50235, 0x04CC025404CC0247, 0x04B0024704C0024F, 
        0x04C902240497023A, 0x04D2023604CA0238, 0x04C0024D04D60238, 0x04AE024504CB023C, 
        0x04A3024804A7022C, 0x049202670498025D, 0x0483028004930269, 0x04B0024004A70251, 
        0xFFCF001DFFC80011, 0xFFC9FFF5FFD30012, 0xFFD1FFF4FFC30000, 0xFFB70026FFCB0020, 
        0xFFE20007FFD50015, 0xFFF5FFDBFFEDFFF0, 0xFFEBFFF0FFF5FFDB, 0xFFD20013FFDF0004, 
        0xFFF3FFECFFE5FFE7, 0x0006FFCC0000FFCE, 0xFFFEFFD00006FFCA, 0xFFE7FFE1FFF2FFE6, 
        0x0003FFE6FFF3FFE2, 0x0012FFDC000DFFDE, 0x000CFFD90011FFD5, 0xFFF7FFC90003FFDF, 
        0x0019FFF70005FFE5, 0x001CFFF2001EFFE9, 0x001EFFEA001FFFE3, 0x0008FFD4001AFFEB, 
        0x0035FFF30016FFF8, 0x0028FFF90036FFE4, 0x0038FFE8002DFFEC, 0x001DFFE1003CFFDC, 
        0x0043FFF8000C0009, 0x0031FFF3003CFFF7, 0x003FFFDE0032FFEA, 0x0012FFEC004FFFCB, 
        0x00150040FFC9004E, 0x0011004900150059, 0x0019001D0015002C, 0xFFD5000400220029,
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
            int phase = 0, staticEval = 0, sq = 64;
            while (--sq >= 0) {
                Piece piece = board.GetPiece(new Square(sq));
                if (!piece.IsNull) {
                    int pieceType = (int)piece.PieceType - 1;
                    staticEval += (piece.IsWhite == board.IsWhiteToMove ? 1 : -1) * (int)(packedEvalWeights[
                        (piece.IsWhite ? sq : sq ^ 56) / 2 + pieceType * 32
                    ] >> sq % 2 * 32);
                    phase += (pieceType + 2 ^ 2) % 5;
                }
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

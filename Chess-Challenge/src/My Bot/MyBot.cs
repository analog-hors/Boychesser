using ChessChallenge.API;
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
        0x0000000000000000, 0x25360b01342b0c02, 0x2222110c2d230f05, 0x191f1f192d271706,
        0x2930291a37372212, 0x474a474555593722, 0x73767277898e5e68, 0x0002000300050003,
        0xfffcffff00030000, 0x322702212a1c290d, 0x23230d1d23192c13, 0x212114252b20260e,
        0x262f25313b2f2b1a, 0x4c58484468583b29, 0x88a55b47b9aa0012, 0x0000000000000000,
        0x5a53292644452901, 0x64643933624f2725, 0x7c6f483e6b5b3c27, 0x8c86454c78684939,
        0x938d4f597e6a4b51, 0x7f877e6c78626745, 0x796b68686a543c3f, 0x6c794b0d69000000,
        0x323112142f372d1d, 0x322d1827332f262d, 0x38371d1f373b2625, 0x333b291b3833222a,
        0x36353031413a221f, 0x2f36463b3c3b3e32, 0x3b3a27293c381d15, 0x49460500423b130a,
        0x6c71372e71732c29, 0x72742f2a7576211a, 0x767b302976783122, 0x8287302e82813027,
        0x888c41408b873c38, 0x848d5f53898b5545, 0x8b8b6865928c4650, 0x7e7e71767c7a766f,
        0x8d7d3d3a848d3c3c, 0x8d8b44458e8d3f3f, 0xaab13c3ca5974142, 0xcbbb363dafae443d,
        0xd9d3393dccae3a49, 0xd4de4d41cab94e4b, 0xdfd83c43d6c9323b, 0xb9af5d64c0c95434,
        0x151f1b1812053028, 0x3c3501112814262a, 0x48430e0d341f1a14, 0x594f1d1f3e262c15,
        0x645b253e4d2d4a37, 0x5d614f5b562f7d66, 0x4a4a6fa04c17a6a9, 0x2124d3d61a00ee7c,
        0x0067003e00650033, 0x010400e100e000ba, 0x031702c901b60112, 0xffeafff3ffebfff5,
        0xfffd000200020001, 0xfffbfffdfffafff1, 0x00000000fffd0005,
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
        var (ttHash, ttMoveRaw, ttScore, ttDepth, ttBound) = tt;

        bool
            ttHit = ttHash == board.ZobristKey,
            nonPv = alpha + 1 == beta,
            inQSearch = depth <= 0,
            pieceIsWhite;
        int
            eval = -0x002fffa, // tempo
            bestScore = -99999,
            oldAlpha = alpha,

            // search loop vars
            moveCount = 0, // quietsToCheckTable = [0, 5, 8, 14, 49]
            quietsToCheck = 0b_110001_001110_001000_000101_000000 >> depth * 6 & 0b111111,

            // static eval vars
            pieceType,

            // temp vars
            score = ttScore,
            tmp = 0;
        if (ttHit) {
            if (ttDepth >= depth && ttBound switch {
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
            void Eval(ulong pieces) {
                // use tmp as phase (initialized above)
                while (pieces != 0) {
                    Square square = new(ClearAndGetIndexOfLSB(ref pieces));
                    Piece piece = board.GetPiece(square);
                    pieceType = (int)piece.PieceType;
                    // virtual pawn type
                    // consider pawns on the opposite half of the king as distinct piece types (piece 0)
                    if (
                        square.File >= 4 != board.GetKingSquare(pieceIsWhite = piece.IsWhite).File >= 4
                            && pieceType == 1
                    )
                        pieceType--;
                    eval += (pieceIsWhite == board.IsWhiteToMove ? 1 : -1) * (
                        // material
                        EvalWeight(112 + pieceType)
                            // psts
                            + (int)(
                                packedData[pieceType * 8 + square.Rank ^ (pieceIsWhite ? 0 : 0b111)]
                                    >> (0x01455410 >> square.File * 4) * 8
                                    & 0xFF00FF
                            )
                            // mobility
                            + EvalWeight(11 + pieceType) * GetNumberOfSetBits(
                                GetSliderAttacks((PieceType)Min(5, pieceType), square, board)
                            )
                            // own pawn on file
                            + EvalWeight(118 + pieceType) * GetNumberOfSetBits(
                                0x0101010101010101UL << square.File
                                    & board.GetPieceBitboard(PieceType.Pawn, pieceIsWhite)
                            )
                    );
                    // phaseWeightTable = [X, 0, 1, 1, 2, 4, 0]
                    tmp += 0x0421100 >> pieceType * 4 & 0xF;
                }
                // note: the correct way to extract EG eval is (eval + 0x8000) / 0x10000, but token count
                eval = ((short)eval * tmp + eval / 0x10000 * (24 - tmp)) / 24;
                // end tmp use
            }
            Eval(board.AllPiecesBitboard);
        }

        if (inQSearch)
            // stand pat in quiescence search
            alpha = Max(alpha, bestScore = eval);
        else if (nonPv && eval >= beta && board.TrySkipTurn()) {
            // Null Move Pruning (NMP)
            bestScore = depth <= 3 ? eval - 44 * depth : -Negamax(-beta, -alpha, (depth * 96 + beta - eval) / 150 - 1, nextPly);
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
            scores[tmp++] -= ttHit && move.RawValue == ttMoveRaw ? 100000
                : Max((int)move.CapturePieceType * 4096 - (int)move.MovePieceType - 2048, HistoryValue(move));
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
            int
                nextDepth = board.IsInCheck() ? depth : depth - 1,
                reduction = Max(
                    move.IsCapture || nextDepth >= depth ? 0
                    : (moveCount * 120 + depth * 103) / 1000 + scores[moveCount] / 256,
                    0
                );
            while (
                moveCount != 0
                    && (score = -Negamax(~alpha, -alpha, nextDepth - reduction, nextPly)) > alpha
                    && reduction != 0
            )
                reduction = 0;
            // end tmp use
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
                // Futility Pruning
                eval + 271 * depth < alpha
            ))
                break;

            moveCount++;
        }

        tt = (
            board.ZobristKey,
            alpha > oldAlpha // if not upper bound
                ? bestMove.RawValue
                : ttMoveRaw,
            (short)bestScore,
            (short)Max(depth, 0),
            (ushort)(
                bestScore >= beta
                    ? 65535 /* BOUND_LOWER */
                    : alpha - oldAlpha /* BOUND_UPPER if alpha == oldAlpha else BOUND_EXACT */
            )
        );
        
        searchBestMove = bestMove;
        return bestScore;
    }

    ref int HistoryValue(Move move) => ref history[
        board.IsWhiteToMove ? 1 : 0,
        (int)move.MovePieceType,
        move.TargetSquare.Index
    ];
}

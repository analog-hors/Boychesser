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
        0x0000000000000000, 0x302b0810202a2901, 0x10151e261d1e2c16, 0x05153c2d2123260e,
        0x1a224839202a4921, 0x373c614e3e405337, 0x6062817270666690, 0x0002000300030007,
        0xfffcfffffffd0003, 0x301a023d1b24470f, 0x111d1924101d4e1c, 0x061936302522270e,
        0x1724474828293f14, 0x3e4a565150464d2d, 0x6a8b6749947a195c, 0x0000000000000000,
        0x1e1d382e1e061a12, 0x27234c44291d332a, 0x3c2866592b2c5325, 0x444864623c2e5552,
        0x4c4975773d2c6767, 0x3f44917e322d9050, 0x4538606637233643, 0x3943320736120c00,
        0x1b1b1a15200e1e2c, 0x1818252e09143c38, 0x201d28301f22362c, 0x1d24312422172443,
        0x1f22362d24183433, 0x1f20392d1e19472e, 0x24220d1b24170f1a, 0x1f2000001e0d023f,
        0x1e1f615831264643, 0x3c374a4739433619, 0x42403c3d3f3f3f31, 0x4c50424150443c47,
        0x57585657574d5457, 0x53586b5e5051715d, 0x4d4f747354495d74, 0x4d4f555949446c78,
        0x374a423b50753d2e, 0x3a33515249484d49, 0x747742496d664a4d, 0xb2a0313d8d6f3e49,
        0xb7a5384593594a60, 0xa4a849407a65645c, 0xae973a51a76b335b, 0x3f23849919019989,
        0x22252c2c01015b56, 0x4e4301142b18353d, 0x5e5505073c292016, 0x6c60282a513c1f14,
        0x78714741674d2e28, 0x7b805c477656383f, 0x716f696f7b533d44, 0x3c47d3aa4d30903f,
        0x0037004d00350044, 0x005e00cb004d00b7, 0x01a1014300a5010c, 0xfff1fff8fff1fff8,
        0xfff2000bfff6000c, 0xffd80002fffcffe8, 0x00000000fffd0008,
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
            eval = 0x0006000f, // tempo
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
                    pieceType -= (square.File ^ board.GetKingSquare(pieceIsWhite = piece.IsWhite).File) >> 1 >> pieceType;
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
                    // phaseWeightTable = [0, 0, 1, 1, 2, 4, 0]
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
            // Delta pruning (23 elo, 21 tokens, 1.1 elo/token)
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
                // LMP (34 elo, 14 tokens, 2.4 elo/token)
                quietsToCheck-- == 1 ||
                // Futility Pruning (11 elo, 8 tokens, 1.4 elo/token)
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

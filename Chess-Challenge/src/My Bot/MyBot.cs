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
        0x0000000000000000, 0x181e140f211c1701, 0x1616221a20151809, 0x121b2c1f241c1808,
        0x252b3426312f2912, 0x595b4d4665583922, 0x919a7868a197625f, 0x0002000400060005,
        0xfffdfffe00050002, 0x2115022115082705, 0x1913101b130a2a0f, 0x14151c2321131a0a,
        0x222d292d352c290f, 0x5e673d498064331b, 0x9bc1644ad5be0c25, 0x0000000000000000,
        0x49404d4a2718413a, 0x5a5152534d40443f, 0x71645b525d464e39, 0x7e7c575568595144,
        0x887f5b64745b5053, 0x7173817962506f4a, 0x695b61695b42433e, 0x57613f0051000000,
        0x2b2a36322d253a3a, 0x312e30392e2a3e3f, 0x3b392c31383d3631, 0x373f32253f362732,
        0x3d3e3430483f2a2b, 0x39403e3b43464132, 0x45411d244239241d, 0x514c00004646040c,
        0x696f5d546e6b4f4d, 0x7572474770754030, 0x808139367d7e3a31, 0x8e9034328d892f2f,
        0x96983d40948f3d3f, 0x939658548e8e5f50, 0x9c9960669c8f4d5f, 0x929169718e8a6f73,
        0xb8a98986b0c8857d, 0xaaa27f80a5b87d7c, 0xb3bf686bc3c26c71, 0xcece5256d5d75c65,
        0xe0df4450e9da5364, 0xe0ec5356dce46b6b, 0xfef13857fef54d65, 0xcbdb6b72e5fd654c,
        0x1a2427271802413a, 0x251c304814055550, 0x2b2823271c144322, 0x3a33121f2b1d2501,
        0x453f1a373b273b14, 0x4948495b4828633c, 0x373b5a5e4216512c, 0x13033f6410005c00,
        0x005a003b0057002c, 0x00da010100ce00f4, 0x036302f10195014b, 0xffecfff7ffebfff9,
        0xfffa000300020001, 0xfff0fffffff3fff0, 0x00060000fffb000a, 0x0000fff800060001,
        0x0005fff70003fffa, 0x00000000ffeffff2,
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
            eval = 0x0008000a, // tempo
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
                    Square opp_king = board.GetKingSquare(!pieceIsWhite);
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
                            // opp king tropism
                            + EvalWeight(125 + pieceType) * Max(
                                Abs(square.File - opp_king.File), Abs(square.Rank - opp_king.Rank)
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

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
        0x0000000000000000, 0x2c32120c35321201, 0x22241b1531261409, 0x1e27241a362d1206,
        0x30372d214141220e, 0x5a5e433c6e693319, 0x979f6e68b7b5565b, 0x0002000300050004,
        0xfffcfffe00030001, 0x2f2907232b222809, 0x2423111c241d2a0f, 0x1f231b2230241b0a,
        0x2e3825284239240d, 0x5f6e3939866c2913, 0xa6d05942ead10026, 0x0000000000000000,
        0x696255504f384b43, 0x79715f5f6f5b5450, 0x8e846c647d666250, 0x9a996f6e88786b60,
        0xa59d787c947d6c6d, 0x95959f9587798c68, 0x8f848b8680695d58, 0x849c6c07872c1a00,
        0x45433332493f3338, 0x464735394647403f, 0x4e4d373b4f50403a, 0x4a524137504c3941,
        0x4e50474159503c3c, 0x4c52564f58575140, 0x5b58343c5c57352e, 0x696c100168660e20,
        0x959a6f689e9a6260, 0x9e9c605f9b9d584e, 0xa5a85b59a5a35c55, 0xafb3615fb4b15858,
        0xb6bc7772bfba6967, 0xb4bb9387bbbf8574, 0xbdbd9c96c6c0797c, 0xb9ba9799bbb99694,
        0x8c8297958ca3948d, 0x968f9a9a919e9a97, 0xb2b89396b2a99799, 0xddd38c91cec09297,
        0xf5f09091eac490a2, 0xf9fb9f9be4d4a9a2, 0xfff495a2fadc8d9c, 0xd3d8bab9e2e4a998,
        0x2026292a1b014344, 0x51460b1e331b3139, 0x675e010449311913, 0x776b1214593f1705,
        0x837c24256f4f2a11, 0x889336268c5e2329, 0x848e3b30984b183c, 0x53519a9255007b78,
        0x00630037005f002b, 0x010e00c300d9009c, 0x042901eb01ba00d1, 0xffecfff8ffedfff9,
        0xfffa000300030002, 0xfff4fffffff4fff3, 0x00000000fffb0008,
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
            eval = 0x00010006, // tempo
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

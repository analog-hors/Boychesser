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
        0x0000000000000000, 0x28291510302d1700, 0x211e291f2c271702, 0x161d34212d291b04,
        0x242c37293c43270f, 0x676c4f518d822d1b, 0xa0b4736cc9d1643c, 0x00070002000b0008,
        0xfffefffa0022fffe, 0x341f01191c112806, 0x241e0a1221132110, 0x17191417291d0e05,
        0x1e2a24283e3b1f10, 0x646f364f9f913814, 0xa1d15d4de0eb2000, 0x0000000000000000,
        0x79703a3b636a3114, 0x8b88544a79763b3f, 0xa4906d5f88785839, 0xaea9736e9580674d,
        0xafac888e9c866c69, 0x9d9da49d8d7a8467, 0x8982769081666455, 0x7978532c6d1e0d00,
        0x4f4f2f344b484940, 0x4d3d314449504746, 0x4b4b363b4d533e3d, 0x3b48482f4f4f313d,
        0x3d465145525c3c38, 0x3c454b54515d504f, 0x47493e3d4d4e4234, 0x615902135b571615,
        0xacb3736bb1b36a69, 0xadab716fabb06960, 0xaeaa6c6ba4af7568, 0xb8bb6d65babc6664,
        0xbcc27a79c2c37476, 0xbbc19291c1c09787, 0xc7cc9ea3ccc19095, 0xbbc2959abfbf9193,
        0xf0e18178f8ff7472, 0xc1bd8d8fc8fe8b85, 0x94a58c90d0ff8c85, 0x77979c8eccff8e85,
        0x7f979c9cd8ff9197, 0xb5b7a1aec8ffb7ae, 0xf9e88a9be6ff91a6, 0xcfdac2bbcbeeb69b,
        0x2027201d11003a2f, 0x4d43071d2f16352f, 0x6155000041291300, 0x76670000512c0000,
        0x81770006663d0000, 0x78704a6668485900, 0x5b64785259376a12, 0x34254a6d16006b0b,
        0x0072004c00700025, 0x00da00b800d000aa, 0x01aa01e8017a00ef, 0xffecfff8ffeefffb,
        0xfffa000700020001, 0x002dfff7fffcffe9, 0x00000000fff70011,
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
            eval = 0x00160016, // tempo
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

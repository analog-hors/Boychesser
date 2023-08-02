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

    public long nodes = 0; // #DEBUG
    public int maxSearchTime, searchingDepth;

    public Timer timer;
    public Board board;

    Move nullMove, searchBestMove, rootBestMove;

    // Assuming the size of TtEntry is indeed 16 bytes, this table is precisely 256MiB.
    TtEntry[] transpositionTable = new TtEntry[0x1000000];

    int[,,] history = new int[2, 7, 64];

    ulong[] packedData = {
        0x0000000000000000, 0x30310b16342c1c05, 0x252616182d241d0c, 0x2129201e352b1509,
        0x313a2923443f220e, 0x5e693e387d6f2d15, 0xa1bc6452d5c52d41, 0x0000000000000000,
        0x6b62524e4e394a42, 0x79725d5a705a504e, 0x8f856b628066604e, 0x9c996d6b8a79695e,
        0xa59e757a957d6a6d, 0x96979e9486798e64, 0x9184888382685a58, 0x879f6d0b862e1802,
        0x45432f3049403135, 0x4648343645483e3d, 0x504e35394f513d3b, 0x4a523f344f4b3640,
        0x4e514640574f3b3d, 0x4c52554e5857523f, 0x5a58343a5c58332d, 0x6a6b120267630e1e,
        0x959b6d669e9a615e, 0x9d9d5e599b9c554c, 0xa5a85a58a6a35b52, 0xafb4615fb3b15759,
        0xb7bc7770c0ba6867, 0xb3bb9486b9bf8674, 0xbcbc9e95c5bf777b, 0xb8ba999abbb79895,
        0x887d959588a1958e, 0x9786959a8f9c999a, 0xb5b98e93ada79699, 0xe4d3858dd1bf8e98,
        0xf7f2898eecc08ea3, 0xfcfe9a97e4d7a8a2, 0xfff7929bfedb8a9d, 0xd1d8bab9e0e0aa9b,
        0x1d24252719003d33, 0x4d440a18311b2b2e, 0x645b020147311209, 0x74681312573e1200,
        0x7f7927226c50240b, 0x859132228c611c1c, 0x849231279c4d1026, 0x565a89835b01676e,
        0x00d80099005f002e, 0x01bb00ce010e00bf, 0x00000000043b01dc, 0x0002000200060004,
        0xfffcfffd00020002, 0x00030003ffecfff9, 0xfff3fff4fffa0004, 0xfffd000bfff30000,
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
        nodes++; // #DEBUG

        // check for game end
        if (board.IsInCheckmate())
            return nextPly - 30000;
        if (board.IsDraw())
            return 0;
        nextPly++;

        ref var tt = ref transpositionTable[board.ZobristKey % 0x1000000];
        bool
            ttHit = tt.hash == board.ZobristKey,
            nonPv = alpha + 1 == beta,
            inQSearch = depth <= 0;
        int
            eval = 0x00000006,
            bestScore = -99999,
            oldAlpha = alpha,

            // search loop vars
            moveCount = 0, // quietsToCheckTable = [0, 8, 7, 16, 50]
            quietsToCheck = 0b_110010_010000_000111_001000_000000 >> depth * 6 & 0b111111,

            // static eval vars
            pieceType,

            // temp vars
            score = tt.score,
            tmp = 0;

        if (ttHit && tt.depth >= depth && tt.bound switch {
            1 /* BOUND_EXACT */ => nonPv || inQSearch,
            2 /* BOUND_LOWER */ => score >= beta,
            3 /* BOUND_UPPER */ => score <= alpha,
        })
            return score;
        else if (!ttHit && depth > 5)
            // Internal Iterative Reduction (IIR)
            depth--;

        // use tmp as phase (initialized above)
        // tempo
        ulong pieces = board.AllPiecesBitboard;
        while (pieces != 0) {
            Square square = new(ClearAndGetIndexOfLSB(ref pieces));
            Piece piece = board.GetPiece(square);
            pieceType = (int)piece.PieceType;
            eval += (piece.IsWhite == board.IsWhiteToMove ? 1 : -1) * (
                // material
                EvalWeight(95 + pieceType)
                    // psts
                    + (int)(
                        packedData[pieceType * 8 - 8 + square.Rank ^ (piece.IsWhite ? 0 : 0b111)]
                            >> (0x01455410 >> square.File * 4) * 8
                            & 0xFF00FF
                    )
                    // mobility
                    + EvalWeight(99 + pieceType) * GetNumberOfSetBits(
                        GetSliderAttacks((PieceType)Min(5, pieceType), square, board)
                    )
                    // own pawn on file
                    + EvalWeight(105 + pieceType) * GetNumberOfSetBits(
                        0x0101010101010101UL << square.File
                            & board.GetPieceBitboard(PieceType.Pawn, piece.IsWhite)
                    )
            );
            // phaseWeightTable = [X, 0, 1, 1, 2, 4, 0]
            tmp += 0x0421100 >> pieceType * 4 & 0xF;
        }
        // note: the correct way to extract EG eval is (eval + 0x8000) / 0x10000, but token count
        eval = ((short)eval * tmp + eval / 0x10000 * (24 - tmp)) / 24;
        // end tmp use

        if (inQSearch)
            // stand pat in quiescence search
            alpha = Max(alpha, bestScore = eval);
        else if (nonPv && beta > -20000 && board.TrySkipTurn()) {
            // Null Move Pruning (NMP)
            bestScore = depth < 4 ? eval - 42 * depth : -Negamax(-beta, -alpha, depth * 2 / 3 - 2, nextPly);
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
            scores[tmp++] -= ttHit && move.RawValue == tt.moveRaw ? 10000
                : move.IsCapture ? (int)move.CapturePieceType * 8 - (int)move.MovePieceType + 5000
                : HistoryValue(move);
        // end tmp use

        Array.Sort(scores, moves);
        Move bestMove = nullMove;
        foreach (Move move in moves) {
            // Delta pruning
            // deltas = [210, 390, 440, 680, 1350]
            // due to sharing of the top bit of each entry with the bottom bit of the next one
            // (expands the range of values for the queen) all deltas must be even (except pawn)
            if (inQSearch && bestScore + (0b1_0101000110_1010101000_0110111000_0110000110_0011010010_0000000000 >> (int)move.CapturePieceType * 10 & 0b1_11111_11111) <= alpha)
                break;

            board.MakeMove(move);
            int nextDepth = board.IsInCheck() ? depth : depth - 1;
            if (moveCount == 0)
                score = -Negamax(-beta, -alpha, nextDepth, nextPly);
            else {
                // use tmp as reduction
                tmp = move.IsCapture || nextDepth >= depth ? 0
                    : (moveCount * 59 + depth * 109) / 1000 + Min(moveCount / 6, 1);
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
                // History Pruning
                scores[moveCount] > 256 ||
                // Futility Pruning
                eval + 300 * depth < alpha
            ))
                break;

            moveCount++;
        }

        tt.bound = (short)(bestScore >= beta ? 2 /* BOUND_LOWER */
            : alpha > oldAlpha ? 1 /* BOUND_EXACT */
            : 3 /* BOUND_UPPER */);
        tt.depth = (short)Max(depth, 0);
        tt.hash = board.ZobristKey;
        tt.score = (short)bestScore;
        if (tt.bound != 3 /* BOUND_UPPER */)
            tt.moveRaw = bestMove.RawValue;

        searchBestMove = bestMove;
        return bestScore;
    }

    ref int HistoryValue(Move move) => ref history[
        board.IsWhiteToMove ? 1 : 0,
        (int)move.MovePieceType,
        (int)move.TargetSquare.Index
    ];
}

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
        short // bound BOUND_EXACT=1, BOUND_LOWER=2, BOUND_UPPER=3
    )[] transpositionTable = new (ulong, ushort, short, short, short)[0x1000000];

    int[,,] history = new int[2, 7, 64];

    ulong[] packedData = {
        0x0000000000000000, 0x2d2e0c152e271a01, 0x21231717281f1d08, 0x1e24211d31251405,
        0x2e382a22403a220a, 0x5b6740397a6a2e12, 0x9dbb6854d4c22b41, 0x0000000000000000,
        0x6761514c4d374840, 0x77715c596e594f4e, 0x8d8369617b645f4d, 0x99986c6b8776675d,
        0xa49d7579927b696b, 0x93959e9284788c64, 0x8f83868380665756, 0x839b6a0686281600,
        0x474532314b413338, 0x484a363748483f3f, 0x5250363b52523f3b, 0x4e554036534e3940,
        0x525447415c523c3e, 0x4f55574f5b595240, 0x5e5b353a5e58342f, 0x6b6d12016a670e22,
        0x979d6e66a19c615e, 0xa0a05f5a9ea0554d, 0xa7ab5b57a8a65b53, 0xb1b6625fb7b45958,
        0xb8be7a72c1bc6b68, 0xb5bd9789bcc18975, 0xbfbf9d96c8c1797e, 0xbbbb989abcbb9895,
        0x867b9392869d938c, 0x958694988a989796, 0xb2b68d90aba49496, 0xe0d1858cccbb8d95,
        0xf6f1898be8bf8ba1, 0xfafb9a96e1d2a79f, 0xfff6909bfbd7879b, 0xcfd5b8b6dddca899,
        0x1c23252918013d34, 0x4c440c18311a2b30, 0x625a02034730110a, 0x72681314573e1300,
        0x7f7a28246e50260b, 0x859234248c611e1e, 0x849032279b51102d, 0x575a89825f026978,
        0x00da009b00600018, 0x01ba00d0011300be, 0x00000000043b01e4, 0x0002000300050004,
        0xfffcfffd00020002, 0x00020002ffeefffd, 0xfff4fff4fffb0003, 0xfffb000efff6ffff,
        0x000600020006000d, 0xfffdfffefffc0003, 0xfffd000b0005fffc,
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
            ttHit = tt.Item1 /* hash */ == board.ZobristKey,
            nonPv = alpha + 1 == beta,
            inQSearch = depth <= 0;
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
            score = tt.Item3 /* score */,
            tmp = 0;

        if (ttHit && tt.Item4 /* depth */ >= depth && tt.Item5 /* bound */ switch {
            1 /* BOUND_EXACT */ => nonPv || inQSearch,
            2 /* BOUND_LOWER */ => score >= beta,
            3 /* BOUND_UPPER */ => score <= alpha,
        })
            return score;
        else if (!ttHit && depth > 5)
            // Internal Iterative Reduction (IIR)
            depth--;

        if (ttHit && !inQSearch)
            eval = score;
        else {
            ulong pieces = board.AllPiecesBitboard, ownPawns;
            // use tmp as phase (initialized above)
            while (pieces != 0) {
                Square square = new(ClearAndGetIndexOfLSB(ref pieces));
                Piece piece = board.GetPiece(square);
                pieceType = (int)piece.PieceType;
                ownPawns = board.GetPieceBitboard(PieceType.Pawn, piece.IsWhite);
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
                            0x0101010101010101UL << square.File & ownPawns
                        )
                        // own pawn on right file
                        + EvalWeight(111 + pieceType) * GetNumberOfSetBits(
                            0x0202020202020202UL << square.File & ownPawns
                        )
                );
                // phaseWeightTable = [X, 0, 1, 1, 2, 4, 0]
                tmp += 0x0421100 >> pieceType * 4 & 0xF;
            }
            // note: the correct way to extract EG eval is (eval + 0x8000) / 0x10000, but token count
            eval = ((short)eval * tmp + eval / 0x10000 * (24 - tmp)) / 24;
            // end tmp use
        }

        if (inQSearch)
            // stand pat in quiescence search
            alpha = Max(alpha, bestScore = eval);
        else if (nonPv && eval >= beta && board.TrySkipTurn()) {
            // Null Move Pruning (NMP)
            bestScore = depth <= 3 ? eval - 44 * depth : -Negamax(-beta, -alpha, depth * 639 / 1000 - 1, nextPly);
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
            scores[tmp++] -= ttHit && move.RawValue == tt.Item2 /* moveRaw */ ? 10000
                : move.IsCapture ? (int)move.CapturePieceType * 8 - (int)move.MovePieceType + 5000
                : HistoryValue(move);
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
            int nextDepth = board.IsInCheck() ? depth : depth - 1;
            if (moveCount != 0) {
                // use tmp as reduction
                tmp = move.IsCapture || nextDepth >= depth ? 0
                    : (moveCount * 76 + depth * 103) / 1000 + Min(moveCount / 7, 1);
                score = -Negamax(~alpha, -alpha, nextDepth - tmp, nextPly);
                if (score > alpha && tmp != 0)
                    score = -Negamax(~alpha, -alpha, nextDepth, nextPly);
                // end tmp use
            }
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
                // History Pruning
                eval <= alpha && scores[moveCount] > 64 * depth ||
                // Futility Pruning
                eval + 271 * depth < alpha
            ))
                break;

            moveCount++;
        }

        // use tmp as bound
        tmp = bestScore >= beta ? 2 /* BOUND_LOWER */
            : alpha > oldAlpha ? 1 /* BOUND_EXACT */
            : 3 /* BOUND_UPPER */;
        tt = (
            board.ZobristKey,
            tmp /* bound */ != 3 /* BOUND_UPPER */
                ? bestMove.RawValue
                : tt.Item2 /* moveRaw */,
            (short)bestScore,
            (short)Max(depth, 0),
            (short)tmp
        );
        // end tmp use
        
        searchBestMove = bestMove;
        return bestScore;
    }

    ref int HistoryValue(Move move) => ref history[
        board.IsWhiteToMove ? 1 : 0,
        (int)move.MovePieceType,
        move.TargetSquare.Index
    ];
}

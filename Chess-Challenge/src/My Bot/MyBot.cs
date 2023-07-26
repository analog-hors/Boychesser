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
    public int maxSearchTime, searchingDepth,
        
        // Temporary static eval variables
        staticEval, negateFeature, featureOffset;

    public Timer timer;
    public Board board;

    Move nullMove, searchBestMove, rootBestMove;

    // Assuming the size of TtEntry is indeed 16 bytes, this table is precisely 256MiB.
    TtEntry[] transpositionTable = new TtEntry[0x1000000];

    short[,,] history = new short[2, 7, 64];

    ulong[] packedEvalWeights = {
        0x012100E000930058, 0x0250011C015700EF, 0x0000000004AD0263,
        0x0005000800190007, 0x0006000900040001, 0x000F000600110002,
        0x000A0007FFFE0000, 0xFFFE000800000000, 0x0010FFE30007FFFF,
        0x00100001FFE2FFFB, 0x0005FFF300010001, 0x0009FFE6000EFFFB,
        0x0000000000000000, 0x0002000200050005, 0xFFFCFFFE00010003,
        0xFFFEFFFEFFFFFFF9, 0xFFFE0004FFFF0000, 0x0000000000020000,
        0x00010003FFEDFFF7, 0xFFF6FFF1FFFA0003, 0xFFFDFFFEFFF50000,
    };

    void AddFeature(int feature) {
        staticEval += (int)(packedEvalWeights[featureOffset / 2] >> featureOffset % 2 * 32) * feature * negateFeature;
        featureOffset += 6;
    }

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
            return 30000 - nextPly;
        nextPly++;

        ref var tt = ref transpositionTable[board.ZobristKey % 0x1000000];
        bool
            ttGood = tt.hash == board.ZobristKey,
            nonPv = alpha + 1 == beta,
            whiteAndIsCapture;
        int oldAlpha = alpha,
            moveCount = 0, // quietsToCheckTable = [0, 7, 8, 17, 49]
            quietsToCheck = 0b_110001_010001_001000_000111_000000 >> depth * 6 & 0b111111,
            bestScore = -999999,
            scoreAndScoreIndex = 0,
            nextDepthAndY,
            reductionAndChangeAndPieceType,
            fileAndNmpResult,
            phaseAndTtScore = tt.score,
            pieceIndex;

        if (ttGood && tt.depth >= depth && tt.bound switch {
            1 => nonPv || depth <= 0,
            2 => phaseAndTtScore >= beta,
            3 => phaseAndTtScore <= alpha,
        })
            return -phaseAndTtScore;

        // Null Move Pruning (NMP)
        if (nonPv && depth >= 1 && board.TrySkipTurn()) {
            fileAndNmpResult = Negamax(-beta, 1 - beta, depth - 3, nextPly);
            board.UndoSkipTurn();
            if (fileAndNmpResult >= beta)
                return -fileAndNmpResult;
        }

    
        // static eval for qsearch
        if (depth <= 0) {
            staticEval = phaseAndTtScore = pieceIndex = 0;
            foreach (PieceList pieceList in board.GetAllPieceLists()) {
                reductionAndChangeAndPieceType = pieceIndex % 6;
                whiteAndIsCapture = pieceIndex < 6;
                negateFeature = whiteAndIsCapture == board.IsWhiteToMove ? 1 : -1;
                foreach (Piece piece in pieceList) {
                    // Maps 0, 1, 2, 3, 4, 5 -> 0, 1, 1, 2, 4, 0 for pieceType
                    phaseAndTtScore += (reductionAndChangeAndPieceType + 2 ^ 2) % 5;
                    Square square = piece.Square;
                    featureOffset = reductionAndChangeAndPieceType;
                    AddFeature(1);
                    AddFeature(nextDepthAndY = whiteAndIsCapture ? square.Rank : 7 - square.Rank);
                    AddFeature(Min(fileAndNmpResult = square.File, 7 - fileAndNmpResult));
                    AddFeature(Min(nextDepthAndY, 7 - nextDepthAndY));
                    AddFeature(
                        BitboardHelper.GetNumberOfSetBits(
                            BitboardHelper.GetSliderAttacks(
                                (PieceType)Min(5, reductionAndChangeAndPieceType + 1),
                                square,
                                board
                            )
                        )
                    );
                    AddFeature(Abs(fileAndNmpResult - board.GetKingSquare(whiteAndIsCapture).File));
                    AddFeature(
                        BitboardHelper.GetNumberOfSetBits(
                            0x0101010101010101UL << fileAndNmpResult
                                & board.GetPieceBitboard(PieceType.Pawn, whiteAndIsCapture)
                        )
                    );
                }
                pieceIndex++;
            }
            alpha = Max(
                alpha,
                bestScore
                    = staticEval
                    = ((short)staticEval * phaseAndTtScore + (staticEval + 0x8000) / 0x10000 * (24 - phaseAndTtScore)) / 24
            );
            if (staticEval >= beta)
                return -staticEval;
        }

        var moves = board.GetLegalMoves(depth <= 0);
        var scores = new int[moves.Length];
        foreach (Move move in moves)
            // sort capture moves by MVV-LVA, quiets by history, and hashmove first
            scores[scoreAndScoreIndex++] -= ttGood && move.RawValue == tt.moveRaw ? 10000
                : move.IsCapture ? (int)move.CapturePieceType * 8 - (int)move.MovePieceType + 5000
                : HistoryValue(move);

        Array.Sort(scores, moves);
        Move bestMove = nullMove;
        foreach (Move move in moves) {
            whiteAndIsCapture = move.IsCapture;
            //LMP
            if (nonPv && depth <= 4 && !whiteAndIsCapture && quietsToCheck-- == 0)
                break;

            board.MakeMove(move);
            nextDepthAndY = board.IsInCheck() ? depth : depth - 1;
            if (board.IsDraw())
                scoreAndScoreIndex = 0;
            else if (moveCount == 0)
                scoreAndScoreIndex = Negamax(-beta, -alpha, nextDepthAndY, nextPly);
            else {
                reductionAndChangeAndPieceType = whiteAndIsCapture || board.IsInCheck() ? 0
                    : (moveCount * 3 + depth * 4) / 40 + Convert.ToInt32(moveCount > 4);
                scoreAndScoreIndex = Negamax(~alpha, -alpha, nextDepthAndY - reductionAndChangeAndPieceType, nextPly);
                if (scoreAndScoreIndex > alpha && reductionAndChangeAndPieceType != 0)
                    scoreAndScoreIndex = Negamax(~alpha, -alpha, nextDepthAndY, nextPly);
                if (scoreAndScoreIndex > alpha && scoreAndScoreIndex < beta)
                    scoreAndScoreIndex = Negamax(-beta, -alpha, nextDepthAndY, nextPly);
            }

            board.UndoMove(move);

            if (scoreAndScoreIndex > bestScore) {
                alpha = Max(alpha, bestScore = scoreAndScoreIndex);
                bestMove = move;
            }
            if (scoreAndScoreIndex >= beta) {
                if (!whiteAndIsCapture) {
                    reductionAndChangeAndPieceType = depth * depth;
                    foreach (Move malusMove in moves.AsSpan(0, moveCount))
                        if (!malusMove.IsCapture)
                            HistoryValue(malusMove) -= (short)(reductionAndChangeAndPieceType + reductionAndChangeAndPieceType * HistoryValue(malusMove) / 4096);
                    HistoryValue(move) += (short)(reductionAndChangeAndPieceType - reductionAndChangeAndPieceType * HistoryValue(move) / 4096);
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
        if (!ttGood || tt.bound != 3 /* BOUND_UPPER */)
            tt.moveRaw = bestMove.RawValue;

        searchBestMove = bestMove;
        return -bestScore;
    }

    ref short HistoryValue(Move move) => ref history[
        board.IsWhiteToMove ? 1 : 0,
        (int)move.MovePieceType,
        (int)move.TargetSquare.Index
    ];
}

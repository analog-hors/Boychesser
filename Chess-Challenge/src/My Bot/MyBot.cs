using ChessChallenge.API;
using System;

// This struct should be 14 bytes large, which with padding means 16 bytes
struct TtEntry {
    public ulong hash;
    public short score;
    public ushort moveRaw;
    public byte depth, bound /* BOUND_EXACT=1, BOUND_LOWER=2, BOUND_UPPER=3 */;
}

public class MyBot : IChessBot {

    public long nodes = 0;
    public int maxSearchTime;

    // Assuming the size of TtEntry is indeed 16 bytes, this table is precisely 256MiB.
    TtEntry[] transposition_table = new TtEntry[0x1000000];
    static int[,] PSTS = new int[6, 64];

    static MyBot() {
        ulong[] PST_BASES = {0xb70083, 0xf4013e, 0x1250161, 0x1ff01e1, 0x3af0405, 0xfff0fff1}, PST_TABLES = {0xcfa7cfa7cfa7cfa7, 0xcfa7cfa7cfa7cfa7, 0xb8b1bbafceafacb4, 0xb9a0f5a9e7a7c0b4, 0xc5a8cba1cbaeb5ab, 0xc39ff0a6d2a2d2a7, 0xdba0caa4cdb0b4b4, 0xb6a6d9aad59fe0a0, 0xe4acd5b4dcbfc1c7, 0xb8b8e0b8dbabe6a5, 0xeeeae9fcd60bc905, 0xbbfbe8f907dc10df, 0x2e2d0c4555543159, 0xc462f14c4d2b133a, 0xcfa7cfa7cfa7cfa7, 0xcfa7cfa7cfa7cfa7, 0xf216d90efef2aa08, 0xfce500f3f713020f, 0x1020071bde11f6fb, 0xf9050e25111223, 0x1d341f240a22fc0e, 0x30f2c112422262f, 0x203e2335171f0613, 0xb13282926362f35, 0x483b263b24280a14, 0x2913252d5830383b, 0x542e382f4f11e40d, 0x3ffc5c127f1c6724, 0x37235b0cea1dca0c, 0x2f11a0d510c2a1c, 0xe209f118baff80eb, 0xa8c204e6b20a5006, 0xf7fffeed09fbebed, 0xf7f3e5ff00f4fffb, 0xc031cfd1bf210f6, 0xde92df521fb1308, 0x1b0e1b0c1b010cf8, 0x16f51efd27071a11, 0x26171911190706fe, 0x10fb1601180e2e0b, 0x3e0d1f10110d0801, 0xa061307310e3112, 0x3403370431fcfc06, 0xa0831043e0a2f02, 0xfff8fa0b1c00f2fc, 0xddf61e0047f72a01, 0xe7fcbaf910efeff6, 0x4ec13f3e2fbf3fd, 0xd00fd04ef03e9f8, 0xe2edd70503f40cfc, 0xf303e801ecfbd0fb, 0xb5fef6f607f8fbf8, 0xeb00ecfce301cffd, 0xdbf1f7f9fcf5fffa, 0xfb05f009e206d804, 0xe5f602f9f5fb05fc, 0x1602030ef104e405, 0xe803f4001f021403, 0x200616080f08f708, 0xcfe39fc29fe0d05, 0x3a0c360e1c0e170c, 0x280416093f044cfe, 0x2f101c13260b1c0e, 0x27061b09050d3b0d, 0x6cef3e3eaddfbd8, 0xcad0dde5e3d9edf4, 0xfee907dbf4e2d9e3, 0xfdd9f9d50be204e9, 0xfafff108fedeeee9, 0x1fe0a03fe0af702, 0xf228f30ce215f3e7, 0xf910ff20f81bfa18, 0xec26ec11e10fe1fc, 0xfd1dfa320d21fb32, 0x42a0302ebffefe5, 0x35022b0c341c1928, 0xfd22f719d50de4e8, 0x32f918173512ec33, 0x814190ffc0fe0f0, 0x290d2703280c3714, 0xd9041bfa33ed00da, 0x1de427f7f30117f3, 0xcf1c0713160410f4, 0x17fe180aff13e41d, 0xe124f91a010c01fc, 0xf4060016f11fe326, 0xe827f4240e0bdefd, 0xdc04ee18e326e12a, 0xf42a0327fb25fe07, 0xeb120129f630f129, 0xff1e112627200619, 0xf91c253b153cfb23, 0x820fb1d0e202c03, 0xf21ae9260b350720, 0xfd1ffd26eccec5, 0x1cfe1113ed1ed704};
        int pst_i = 0, piece;
        while (pst_i < 384) {
            piece = pst_i / 64;
            var n = PST_TABLES[pst_i / 4] >> pst_i % 4 * 16;
            PSTS[piece, pst_i++ % 64] = (int)PST_BASES[piece]
                + (int)((short)n >> 8)
                + ((int)(sbyte)n << 16);
        }
    }

    public Move Think(Board board, Timer timer) {
        nodes = 0;
        maxSearchTime = timer.MillisecondsRemaining / 80;

        Move best = Move.NullMove;

        for (int depth = 1; depth <= 200; depth++) {
            //If score is of this value search has been aborted, DO NOT use result
            try {
                (int score, Move bestMove) = Negamax(board, -999999, 999999, depth, timer, depth, 0);
                best = bestMove;
                //Use for debugging, commented out because it saves a LOT of tokens!!
                //Console.WriteLine("info depth " + depth + " score cp " + score);
            } catch (Exception) {
                break;
            }
        }

        return best;
    }

    public (int, Move) Negamax(Board board, int alpha, int beta, int depth, Timer timer, int searchingDepth, int ply) {
        //abort search
        if (timer.MillisecondsElapsedThisTurn >= maxSearchTime && searchingDepth > 1) {
            throw new Exception();
        }

        //node count
        nodes++;

        // check for game end
        if (board.IsInCheckmate()) {
            return (-30000, Move.NullMove);
        }

        ref var tt = ref transposition_table[board.ZobristKey % 0x1000000];
        bool tt_good = tt.hash == board.ZobristKey;

        if (tt_good && tt.depth >= depth && ply > 0) {
            if (tt.bound == 1 /* BOUND_EXACT */ ||
                    tt.bound == 2 /* BOUND_LOWER */ && tt.score >= beta ||
                    tt.bound == 3 /* BOUND_UPPER */ && tt.score <= alpha) {
                return (tt.score, Move.NullMove);
            }
        }

        int bestScore = -999999;
        bool raisedAlpha = false;

        // static eval for qsearch
        int staticEval = 0;
        if (depth <= 0) {
            var pieceLists = board.GetAllPieceLists();
            int mgPhase = Math.Min(
                pieceLists[1].Count + pieceLists[7].Count + // knights
                pieceLists[2].Count + pieceLists[8].Count + // bishops
                (pieceLists[3].Count + pieceLists[9].Count) * 2 + // rooks
                (pieceLists[4].Count + pieceLists[10].Count) * 4, // queens
                24
            );
            foreach (var list in pieceLists) {
                foreach (var piece in list) {
                    var n = PSTS[(int)list.TypeOfPieceInList - 1, piece.Square.Index ^ (list.IsWhitePieceList ? 0 : 0b111000)];
                    if (board.IsWhiteToMove == list.IsWhitePieceList) {
                        staticEval += n;
                    } else {
                        staticEval -= n;
                    }
                }
            }
            // staticEval = (int)(short)staticEval;
            staticEval = ((short)staticEval * mgPhase + (staticEval >> 16) * (24 - mgPhase)) / 24;
            if (staticEval >= beta) {
                return (staticEval, Move.NullMove);
            }
            if (staticEval > alpha) {
                raisedAlpha = true;
                alpha = staticEval;
            }
            bestScore = staticEval;
        }

        var moves = board.GetLegalMoves(depth <= 0);
        var scores = new int[moves.Length];
        for (int i = 0; i < moves.Length; i++) {
            // sort moves MVV-LVA
            scores[i] = tt_good && moves[i].RawValue == tt.moveRaw ? 10000 :
                (int)moves[i].CapturePieceType * 8 - (int)moves[i].MovePieceType;
            scores[i] *= -1;
        }

        Array.Sort(scores, moves);
        Move bestMove = Move.NullMove;
        foreach (Move move in moves) {
            board.MakeMove(move);
            var score = board.IsDraw() ? 0 : -Negamax(board, -beta, -alpha, depth - 1, timer, searchingDepth, ply + 1).Item1;
            board.UndoMove(move);

            if (score > bestScore) {
                bestScore = score;
                bestMove = move;
            }
            if (score >= beta) {
                break;
            }
            if (score > alpha) {
                raisedAlpha = true;
                alpha = score;
            }
        }

        tt.bound = (byte)(bestScore >= beta ? 2 /* BOUND_LOWER */
            : raisedAlpha ? 1 /* BOUND_EXACT */
            : 3 /* BOUND_UPPER */);
        tt.depth = (byte)(depth < 0 ? 0 : depth);
        tt.hash = board.ZobristKey;
        tt.score = (short)bestScore;
        tt.moveRaw = bestMove.RawValue;

        return (bestScore, bestMove);
    }
}
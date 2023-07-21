using ChessChallenge.API;
using ChessChallenge.Chess;
using System;
using System.Diagnostics;

namespace Uci {
    internal class Uci {
        ChessChallenge.Chess.Board board;
        MyBot bot;

        public Uci() {
            bot = new MyBot();
        }

        public void Run(string[] args) {
            if (args.Length > 0 && args[0] == "bench") {
                string[] benches = new string[]{    
                    "Q7/5Q2/8/8/3k4/6P1/6BP/7K b - - 0 67",
                    "r4rk1/p4ppp/1q2p3/2n1P3/2p5/3bRNP1/1P3PBP/R2Q2K1 b - - 0 24",
                    "r1bq1rk1/pp3ppp/2nbpn2/3p4/3P4/1PN1PN2/1BP1BPPP/R2Q1RK1 b - - 2 10",
                    "1r4k1/1P3p2/6pp/2Pp4/4P3/PQ1K1R2/6P1/4q3 w - - 0 51",
                    "8/8/R7/4n3/4k3/6P1/6K1/8 w - - 68 164",
                    "2r3k1/1b4bp/1p2p1p1/3pNp2/3P1P1q/PB1Q3P/1P4P1/4R1K1 w - - 2 36",
                    "4rrk1/1b4bp/p1p1p1p1/3pN3/1P3q2/PQN3P1/2P1RP1P/3R2K1 b - - 0 24",
                    "rnbq1rk1/ppp1bppp/4p3/3pP1n1/2PP3P/5PP1/PP4B1/RNBQK1NR b KQ - 0 8",
                    "3r1r1k/p1p3pp/2p5/8/4K3/2N3Pb/PPP5/R1B4R b - - 0 20",
                    "r4k1r/ppq2ppp/4bB2/8/2p5/4P3/P3BPPP/1R1Q1RK1 b - - 0 17",
                    "r4rk1/1b1nq1pp/p7/3pNp2/1p3Q2/3B3P/PPP1N1R1/R2K4 w - - 2 21",
                    "8/5p2/8/p6k/8/3N4/5PPK/8 w - - 0 49",
                    "2r1rbk1/4pp1p/1Q1P1np1/2B1Nq2/P4P2/1B3P2/1PP3bP/1K1RR3 b - - 0 29",
                    "6k1/p4ppp/Bpp5/4P3/P7/4QKPb/2P3N1/3r3q w - - 5 36",
                    "3br1k1/pp1r1ppp/3pbn2/P2Np3/1PPpP3/3P1NP1/5PBP/3RR1K1 w - - 1 21",
                    "8/1p6/p3n3/4k3/8/6PR/1rr5/3R2K1 w - - 8 54",
                    "1r4k1/p4p1p/5p2/8/4P3/4K3/PPP3P1/4R3 w - - 0 34",
                    "6k1/6p1/7p/7R/7P/5n2/P3K1b1/8 b - - 2 48",
                    "2rr2k1/pp5p/3p4/4p3/2b1p3/P4QP1/1P4P1/3R2K1 w - - 0 28",
                    "q1r4k/1bR5/rp4pB/3p4/3P2nQ/8/PP3PPP/R5K1 w - - 1 29",
                    "rnbqkbnr/pppppp1p/6p1/8/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2",
                    "rnbqk1nr/1p3ppp/p3p3/2bp4/4P3/5N2/PPPN1PPP/R1BQKB1R w KQkq - 0 6",
                    "r2q1rk1/1p1b1p1p/p5p1/3QP3/8/5N2/PP3PPP/2KR3R b - - 0 20",
                    "r3r2k/pbp1q2p/1p6/4n3/2NQ4/2P2pB1/P1P2P1P/2R2RK1 b - - 6 26",
                    "8/1p2k3/4rp2/p2R3Q/2q2B2/6P1/5P1P/6K1 b - - 14 73"
                };

                var timer = new Stopwatch();
                timer.Start();

                int nodeCount = 0;
                board = new ChessChallenge.Chess.Board();
                for (int i = 0; i < benches.Length; i++) {
                    board.LoadPosition(benches[i]);
                    bot.BenchSearch(new ChessChallenge.API.Board(board));
                    nodeCount += bot.GetNodeCount();
                }

                timer.Stop();

                Console.WriteLine(nodeCount + " nodes " + (nodeCount / (timer.ElapsedMilliseconds / 1000)) + " nps");
                return;
            }
            while (true) {
                var line = Console.ReadLine() ?? "quit";
                var tokens = line.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
                switch (tokens[0]) {
                    case "uci": {
                        Console.WriteLine("id name BoyChesser");
                        Console.WriteLine("id author you like chessing boys don't you");
                        Console.WriteLine("uciok");
                        break;
                    }
                    case "ucinewgame": {
                        bot = new MyBot();
                        break;
                    }
                    case "isready": {
                        Console.WriteLine("readyok");
                        break;
                    }
                    case "position": { // position [startpos | fen <fen>] moves <moves>
                        board = new ChessChallenge.Chess.Board();
                        board.LoadStartPosition();
                        if (tokens[1] == "fen") {
                            var fen = tokens[2];
                            for (int i = 3; i < 8; i++) {
                                fen += " " + tokens[i];
                            }
                            board.LoadPosition(fen);
                        }
                        var moveIndex = Array.FindIndex(tokens, t => t == "moves");
                        if (moveIndex != -1) {
                            for (int i = moveIndex + 1; i < tokens.Length; i++) {
                                var move = MoveUtility.GetMoveFromUCIName(tokens[i], board);
                                board.MakeMove(move, true);
                            }
                        }
                        break;
                    }
                    case "go": {
                        var wtime = int.Parse(tokens[Array.FindIndex(tokens, t => t == "wtime") + 1]);
                        var btime = int.Parse(tokens[Array.FindIndex(tokens, t => t == "btime") + 1]);
                        var timer = new Timer(board.IsWhiteToMove ? wtime : btime);
                        var move = bot.Think(new ChessChallenge.API.Board(board), timer);
                        var moveStr = MoveUtility.GetMoveNameUCI(new ChessChallenge.Chess.Move(move.RawValue));
                        Console.WriteLine("bestmove " + moveStr);
                        break;
                    }
                    case "quit": {
                        return;
                    }
                    default: {
                        throw new InvalidOperationException("unknown uci command");
                    }
                }
            }
        }
    }
}

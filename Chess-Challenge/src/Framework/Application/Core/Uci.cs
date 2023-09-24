using ChessChallenge.API;
using ChessChallenge.Chess;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace Uci {
    internal class Uci {
        ChessChallenge.Chess.Board board;
        MyBot bot;

        int[] weights = new int[486];
        bool updateWeights = true;

        public Uci() {
            bot = new MyBot();
        }

        public void Run() {
            while (true) {
                var line = Console.ReadLine() ?? "quit";
                var tokens = line.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
                switch (tokens[0]) {
                    case "uci": {
                        Console.WriteLine("id name BoyChesser");
                        Console.WriteLine("id author you like chessing boys don't you");

                        for (int i = 0; i < weights.Length; i++) {
                            Console.WriteLine(
                                $"option name P_{i} type spin default 0 min -999999 max 999999"
                            );
                        }

                        Console.WriteLine("uciok");
                        break;
                    }
                    case "setoption": {
                        var name = tokens[Array.FindIndex(tokens, t => t == "name") + 1];
                        if (name.StartsWith("P_")) {
                            var index = int.Parse(name[2..]);
                            var value = int.Parse(tokens[Array.FindIndex(tokens, t => t == "value") + 1]);
                            weights[index] = value;
                            updateWeights = true;
                        }
                        break;
                    }
                    case "ucinewgame": {
                        bot = new MyBot();
                        break;
                    }
                    case "isready": {
                        GC.Collect();
                        Console.WriteLine("readyok");
                        break;
                    }
                    case "position": { // position [startpos | fen <fen>] moves <moves>
                        if (updateWeights) {
                            updateWeights = false;
                            using var process = new Process();
                            process.StartInfo.FileName = "python3";
                            process.StartInfo.UseShellExecute = false;
                            process.StartInfo.RedirectStandardOutput = true;
                            process.StartInfo.ArgumentList.Add("-c");
                            process.StartInfo.ArgumentList.Add(Pack.PACKER);
                            foreach (var weight in weights) {
                                process.StartInfo.ArgumentList.Add(weight.ToString());
                            }
                            process.Start();
                            var output = process.StandardOutput.ReadToEnd();
                            process.WaitForExit();

                            var matches = Regex.Matches(output, "0x[0-9a-f]+")
                                .Select(w => Convert.ToUInt64(w.Value, 16))
                                .GetEnumerator();
                            if (!matches.MoveNext()) {
                                throw new Exception("not enough items?");
                            }
                            MyBot.tempo = (int)matches.Current;
                            for (int i = 0; i < MyBot.packedData.Length; i++) {
                                if (!matches.MoveNext()) {
                                    throw new Exception("not enough items?");
                                }
                                MyBot.packedData[i] = matches.Current;
                            }
                            if (matches.MoveNext()) {
                                throw new Exception("too many items?");
                            }
                        }

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
                                board.MakeMove(move, false);
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

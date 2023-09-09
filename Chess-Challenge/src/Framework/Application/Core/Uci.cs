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

        public void Run() {
            while (true) {
                var line = Console.ReadLine() ?? "quit";
                var tokens = line.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
                switch (tokens[0]) {
                    case "uci": {
                        Console.WriteLine("id name BoyChesser");
                        Console.WriteLine("id author you like chessing boys don't you");

                        foreach (var field in typeof(Params).GetFields()) {
                            if (field.IsStatic) {
                                Console.WriteLine(
                                    "option name P_{0} type spin default {1} min -9999 max 9999",
                                    field.Name,
                                    field.GetValue(null)
                                );
                            }
                        }

                        Console.WriteLine("uciok");
                        break;
                    }
                    case "setoption": {
                        var name = tokens[Array.FindIndex(tokens, t => t == "name") + 1];
                        if (name.StartsWith("P_")) {
                            var field = name.Substring(2);
                            var value = int.Parse(tokens[Array.FindIndex(tokens, t => t == "value") + 1]);
                            typeof(Params).GetField(field).SetValue(null, value);
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

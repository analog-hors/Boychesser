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
                        Console.WriteLine("uciok");
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

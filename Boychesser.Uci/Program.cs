using System.Text.RegularExpressions;
using ChessChallenge.Chess;

if (args.Length > 0) {
    if (args.Length == 1 && args[0] == "bench") {
        Bench.RunBench();
        return 0;
    } else {
        Console.Error.WriteLine("invalid subcommand");
        return 1;
    }
}

var board = new Board();
var bot = new MyBot();
while (true) {
    var line = Console.ReadLine() ?? "quit";
    var tokens = Regex.Split(line, @"\s+");
    var index = 0;
    string? Next() => index < tokens.Length ? tokens[index++] : null;

    switch (Next()) {
        case "uci": {
            Console.WriteLine("id name Boychesser");
            Console.WriteLine("id author Boychesser Team");
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
        case "position": {
            board = new Board();
            board.LoadPosition(Next() switch {
                "startpos" => FenUtility.StartPositionFEN,
                "fen" => $"{Next()} {Next()} {Next()} {Next()} {Next()} {Next()}",
                var tok => throw new InvalidOperationException($"unexpected token {tok}"),
            });
            if (Next() == "moves") {
                string? tok;
                while ((tok = Next()) != null) {
                    var move = MoveUtility.GetMoveFromUCIName(tok, board);
                    board.MakeMove(move, false);
                }
            }
            break;
        }
        case "go": {
            var wtime = 0;
            var btime = 0;
            var winc = 0;
            var binc = 0;
            string? tok;
            while ((tok = Next()) != null) {
                switch (tok) {
                    case "wtime": {
                        wtime = int.Parse(Next()!);
                        break;
                    }
                    case "btime": {
                        btime = int.Parse(Next()!);
                        break;
                    }
                    case "winc": {
                        winc = int.Parse(Next()!);
                        break;
                    }
                    case "binc": {
                        binc = int.Parse(Next()!);
                        break;
                    }
                    default: {
                        break;
                    }
                }
            }
            var move = bot.Think(
                new ChessChallenge.API.Board(board),
                board.IsWhiteToMove
                    ? new ChessChallenge.API.Timer(wtime, btime, wtime, winc)
                    : new ChessChallenge.API.Timer(btime, wtime, btime, binc)
            );
            var moveStr = MoveUtility.GetMoveNameUCI(new Move(move.RawValue));
            Console.WriteLine($"bestmove {moveStr}");
            break;
        }
        case "quit": {
            return 0;
        }
        default: {
            throw new InvalidOperationException("unknown uci command");
        }
    }
}

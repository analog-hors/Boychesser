using System.Text.RegularExpressions;
using ChessChallenge.Chess;

Console.Error.WriteLine("you like chessing boys don't you");

if (args.Length > 0) {
    if (args.Length == 1 && args[0] == "bench") {
        Bench.RunBench();
        return 0;
    } else {
        Console.Error.WriteLine("invalid subcommand");
        return 1;
    }
}

const ulong MIB = 1024 * 1024;
ulong hash = 16;
var board = new Board();
var bot = new MyBot(hash * MIB);
while (true) {
    var command = Console.ReadLine() ?? "quit";
    var tokens = Regex.Split(command, @"\s+");
    IEnumerable<string> SkipPast(string tok) => tokens.SkipWhile(t => t != tok).Skip(1);

    switch (tokens[0]) {
        case "uci": {
            Console.WriteLine("id name Boychesser");
            Console.WriteLine("id author Boychesser Team");
            Console.WriteLine($"option name Hash type spin default {hash} min 1 max 1048576");
            Console.WriteLine("uciok");
            break;
        }
        case "setoption": {
            var name = SkipPast("name").First();
            var value = SkipPast("value").First();
            switch (name) {
                case "Hash": {
                    hash = ulong.Parse(value);
                    bot = new MyBot(hash * MIB);
                    break;
                }
                default: {
                    throw new InvalidOperationException($"invalid option {name}");
                }
            }
            break;
        }
        case "ucinewgame": {
            bot = new MyBot(hash * MIB);
            break;
        }
        case "isready": {
            Console.WriteLine("readyok");
            break;
        }
        case "position": {
            board = new Board();
            string fen = string.Join(' ', SkipPast("fen").Take(6));
            board.LoadPosition(fen != "" ? fen : FenUtility.StartPositionFEN);
            foreach (var moveStr in SkipPast("moves")) {
                var move = MoveUtility.GetMoveFromUCIName(moveStr, board);
                board.MakeMove(move, false);
            }
            break;
        }
        case "go": {
            var wtime = SkipPast("wtime").Select(int.Parse).FirstOrDefault();
            var btime = SkipPast("btime").Select(int.Parse).FirstOrDefault();
            var winc = SkipPast("winc").Select(int.Parse).FirstOrDefault();
            var binc = SkipPast("binc").Select(int.Parse).FirstOrDefault();
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
            Console.Error.WriteLine("unknown uci command");
            break;
        }
    }
}

using ChessChallenge.API;

public class MyBot : IChessBot
{
    
    public Move Think(Board board, Timer timer)
    {
        var rand = new System.Random();
        Move[] moves = board.GetLegalMoves();
        return moves[rand.Next(moves.Length)];
    }
}
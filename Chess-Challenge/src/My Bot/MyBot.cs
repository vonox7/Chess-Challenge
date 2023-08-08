using System;
using System.Linq;
using System.Security.Cryptography;
using ChessChallenge.API;
using static ChessChallenge.Application.ConsoleHelper;

public class MyBot : IChessBot
{
    int[] pieceValues = { 0, 100, 300, 300, 500, 900, 10000 }; // TODO which values?
    
    public Move Think(Board board, Timer timer)
    {
        Move[] moves = board.GetLegalMoves(); // TODO: non-alloc
        Random rng = new();
        var whiteToMove = board.IsWhiteToMove;

        var bestMove = moves[0];
        var bestScore = whiteToMove ? -100000.0 : 100000.0;

        foreach (var move in moves)
        {
            board.MakeMove(move);
            var score = evaluate(board, true) - evaluate(board, false);
            //Log($"    {move.MovePieceType} {move}: {score}");
            // Add small random, so we get less often stalemate
            score += rng.NextDouble() / 1000.0;
            if (whiteToMove ? score > bestScore : score < bestScore)
            {
                bestScore = score;
                bestMove = move;
            }
            board.UndoMove(move);
        }

        //Log($"--> {bestMove.MovePieceType} {bestMove}: {bestScore}");
        return bestMove;
    }

    double evaluate(Board board, bool white)
    {
        // Checkmate is of course always best
        if (board.IsInCheckmate())
        {
            return board.IsWhiteToMove == white ? Double.MinValue : Double.MaxValue;
        }
        
        var score = 0.0;

        var kingSquare = board.GetKingSquare(white);
        
        foreach (var pieceList in board.GetAllPieceLists())
        {
            if (white != pieceList.IsWhitePieceList) continue;
            foreach (var piece in pieceList)
            {
                score += pieceValues[(int) piece.PieceType];
                
                if (piece.IsPawn)
                {
                    // Make pawns move forward
                    var rank = piece.Square.Rank;
                    var ranksAwayFromPromotion = white ? rank : 7 - rank;
                    score += ranksAwayFromPromotion * ranksAwayFromPromotion; // TODO square?
                    
                    // King wants to have 3 pawns infront of him
                    // (5 = as long as game is not progressed a lot)
                    // But only if king has moved away from start position (and has not moved to center)
                    if (pieceList.Count >= 5 && kingSquare.File != 3 && kingSquare.File != 4)
                    {
                        if (piece.Square.Rank == kingSquare.Rank + (white ? 1 : -1) &&
                            Math.Abs(piece.Square.File - kingSquare.File) < 2)
                        {
                            score += 10; // TODO how much score?
                        }
                    }
                }
                
                // TODO blockers:0 vs board --> actually attacking ones vs pinned
                var attacks = BitboardHelper.GetPieceAttacks(piece.PieceType, piece.Square, board, pieceList.IsWhitePieceList);
                
                // Move pieces to places with much freedom TODO up to how much freedom is it relevant?
                // TODO freedom is more important, should lead to moving pawn forward after castling
                // TODO weight bei how "relevant" is attacking/protecting piece
                score += 5 * BitboardHelper.GetNumberOfSetBits(attacks);
                
                // Make pieces protect other pieces AND Make pieces attacking other pieces. (TODO: same score? which score?)
                // This includes pinning.
                score += 1.5 * BitboardHelper.GetNumberOfSetBits(attacks & board.AllPiecesBitboard);
            }
        }

        // Allow early castle
        // TODO how long? which score? degressive based on PlyCount? same score?
        if (board.PlyCount < 30)
        {
            score += board.HasKingsideCastleRight(white) ? 10 : 0;
            score += board.HasQueensideCastleRight(white) ? 10 : 0;
            if (kingSquare.File != 4 && kingSquare.Rank == (white ? 0 : 7))
            {
                score += 40; // King has castled, we like this (we must like this more than 2 castle rights combined)
            }
        }
        
        // Putting someone in check is quite often good TODO but how good?
        if (board.IsInCheck())
        {
            score += board.IsWhiteToMove == white ? -50 : 50;
        }
        
        return score; // 13900 = everything
    }
}
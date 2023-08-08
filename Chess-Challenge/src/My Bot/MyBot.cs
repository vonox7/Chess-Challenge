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

        //Log($"--> {bestMove.MovePieceType} {bestMove}: {Math.Round(bestScore)}");
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

        //var kingSquare = board.GetKingSquare(white);
        
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
                    score += 3 * ranksAwayFromPromotion;
                }
                
                var attacks = BitboardHelper.GetPieceAttacks(piece.PieceType, piece.Square, board, pieceList.IsWhitePieceList);
                
                // Move pieces to places with much freedom TODO up to how much freedom is it relevant?
                // TODO freedom is more important, should lead to moving pawn forward after castling
                // TODO weight bei how "relevant" is attacking/protecting piece
                //score += 2 * BitboardHelper.GetNumberOfSetBits(attacks);
                
                // TODO Make pieces protect other pieces 
                // TODO Pinning
                
                // Make pieces attacking other pieces
                score += 1.5 * BitboardHelper.GetNumberOfSetBits(attacks & board.AllPiecesBitboard);
            }
        }

        // TODO favour early castle & castle rights
        
        // Putting someone in check is quite often good
        if (board.IsInCheck())
        {
            score += board.IsWhiteToMove == white ? 70 : -70;
        }
        
        return score; // 13900 = everything
    }
}
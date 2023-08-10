using System;
using System.Linq;
using System.Security.Cryptography;
using ChessChallenge.API;
using ChessChallenge.Chess;
using Microsoft.CodeAnalysis;
using static ChessChallenge.Application.ConsoleHelper;
using Board = ChessChallenge.API.Board;
using Move = ChessChallenge.API.Move;

public class MyBot : IChessBot
{
    Board board;
    Timer timer;
    private Move bestMove;
    int[] pieceValues = { 0, 100, 300, 300, 500, 900, 10000 }; // TODO which values?
    
    public Move Think(Board _board, Timer _timer)
    {
        board = _board;
        timer = _timer;
        bestMove = Move.NullMove; // TODO FIXME is sometimes still null after minimax?
        // TODO sort for ab-pruning: "Check - Capture - Attack": bestMove.IsPromotion; bestMove.IsCapture; bestMove.IsCastles; attack(make move; check if attack & ~currentAttack count > 0)
        // TODO run those "good" branches +2 depth
        
        minimax(3, board.IsWhiteToMove, true);
        
        // TODO: non-alloc

        return bestMove;
    }

    double minimax(int depth, bool whiteToMinimize, bool assignBestMove)
    {
        if (depth == 0 || board.IsInCheckmate() || board.IsDraw()) // TODO 3 cases different?
        {
            return evaluate();
        }

        if (whiteToMinimize)
        {
            var maxEval = -1000000000.0; // TODO extract function for both cases to spare code?
            foreach (var move in board.GetLegalMoves())
            {
                board.MakeMove(move);
                var eval = minimax(depth - 1, false, false);
                board.UndoMove(move);
                if (eval > maxEval)
                {
                    maxEval = eval;
                    if (assignBestMove) bestMove = move;
                }
            }

            return maxEval;
        }
        else
        {
            var minEval = 1000000000.0;
            foreach (var move in board.GetLegalMoves())
            {
                board.MakeMove(move);
                var eval = minimax(depth - 1, true, false);
                board.UndoMove(move);
                if (eval < minEval)
                {
                    minEval = eval;
                    if (assignBestMove) bestMove = move;
                }
            }

            return minEval;
        }
    }

    double evaluate()
    {
        return evaluate(board, true) - evaluate(board, false);
    }

    double evaluate(Board board, bool white)
    {
        // Checkmate is of course always best
        if (board.IsInCheckmate())
        {
            return board.IsWhiteToMove == white ? Double.MinValue : Double.MaxValue;
        }
        
        var score = 0.0;
        
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

                /*if (piece.IsKing/* && board.PlyCount > 20  * /)
                {
                    // King safety is very important (after move 10?)
                    var kingAttacksAfterOneMove = attacks & ~board.AllPiecesBitboard;
                    var kingAttackIteration = kingAttacksAfterOneMove;
                    while (kingAttackIteration != 0)
                    {
                        var squareIndex = BitboardHelper.ClearAndGetIndexOfLSB(ref kingAttackIteration);
                        kingAttacksAfterOneMove |= BitboardHelper.GetKingAttacks(new Square(squareIndex)) & ~board.AllPiecesBitboard;
                    }
                    var add = Math.Max(board.PlyCount - 10, 0) *
                             Math.Min(BitboardHelper.GetNumberOfSetBits(kingAttacksAfterOneMove), 2); // 4 = save enough TODO
                    score += add;
                    // TODO remove the places that are under attack by opponent?
                }*/
                
                
                // Move pieces to places with much freedom TODO up to how much freedom is it relevant?
                // TODO freedom is more important, should lead to moving pawn forward after castling
                // TODO weight bei how "relevant" is attacking/protecting piece
                //score += 2 * BitboardHelper.GetNumberOfSetBits(attacks);
                
                // TODO Make pieces protect other pieces 
                // TODO Pinning
                
                // Make pieces attacking/defending other pieces TODO same score for attack+defense?
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
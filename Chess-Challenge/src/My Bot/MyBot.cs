using System;
using System.Collections.Immutable;
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

        minimax(4, board.IsWhiteToMove, -1000000000.0, 1000000000.0, true);

        if (bestMove == Move.NullMove)
        {
            bestMove = board.GetLegalMoves().First();
        }

        // TODO: non-alloc

        return bestMove;
    }

    bool isHighPotentialMove(Move move)
    {
        // TODO check also for check - at least after 10 plys because then we are faster?
        return move.IsCapture || move.IsPromotion || move.IsCastles;
    }
    
    double minimax(int depth, bool whiteToMinimize, double alpha, double beta, bool assignBestMove)
    {
        if (depth == 0 || board.IsInCheckmate() || board.IsDraw()) // TODO 3 cases different?
        {
            return evaluate();
        }

        if (whiteToMinimize)
        {
            var maxEval = -1000000000.0; // TODO extract function for both cases to spare code?
            var moves = board.GetLegalMoves();
            
            // Optimize ab-pruning: first check moves that are more likely to be good
            moves = moves.Where(move => isHighPotentialMove(move))
                .Concat(moves.Where(move => !isHighPotentialMove(move)))
                .ToArray();
            
            foreach (var move in moves)
            {
                board.MakeMove(move);
                var eval = minimax(depth - 1, false, alpha, beta, false);
                board.UndoMove(move);
                alpha = Math.Max(alpha, eval);
                if (eval > maxEval)
                {
                    maxEval = eval;
                    if (assignBestMove) bestMove = move;
                }

                if (beta <= alpha) break;
            }

            return maxEval;
        }
        else
        {
            var minEval = 1000000000.0;
            foreach (var move in board.GetLegalMoves())
            {
                board.MakeMove(move);
                var eval = minimax(depth - 1, true, alpha, beta, false);
                board.UndoMove(move);
                beta = Math.Min(beta, eval);
                if (eval < minEval)
                {
                    minEval = eval;
                    if (assignBestMove) bestMove = move;
                }

                if (beta <= alpha) break;
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
                score += pieceValues[(int)piece.PieceType];

                if (piece.IsPawn)
                {
                    // Make pawns move forward
                    var rank = piece.Square.Rank;
                    var ranksAwayFromPromotion = white ? rank : 7 - rank;
                    score += 3 * ranksAwayFromPromotion;
                } // TODO endgame evaluation: king in center vs side/top/bottom (or near other pieces, no matter of color): board weight + 1 center-weight

                var attacks =
                    BitboardHelper.GetPieceAttacks(piece.PieceType, piece.Square, board, pieceList.IsWhitePieceList);

                // Move pieces to places with much freedom TODO up to how much freedom is it relevant? bishop < 2 freedom = trapped = very bad
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
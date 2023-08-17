using System;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ChessChallenge.API;
using Board = ChessChallenge.API.Board;
using Move = ChessChallenge.API.Move;

public class MyBot : IChessBot
{
    Board board;
    Timer timer;
    private Move bestMove;
    private double bestMoveEval;
    int[] pieceValues = { 0, 100, 300, 300, 500, 900, 10000 }; // TODO which values?
    private int[] backRankPieceNegativeScore = { 0, 0, -30, -25, 0, -20, 0 };
    int maxExpectedMoveDuration;
    private double[] overshootFactor = { 1, 1, 1, 1 };

    public Move Think(Board _board, Timer _timer)
    {
        board = _board;
        timer = _timer;
        bestMove = Move.NullMove;
        maxExpectedMoveDuration = 10000000;

        // Time control
        var depth = 8;
        var pieceCountSquare = BitboardHelper.GetNumberOfSetBits(board.BlackPiecesBitboard) * BitboardHelper.GetNumberOfSetBits(board.WhitePiecesBitboard);
        var averageOvershootFactor = overshootFactor.Sum() / 4;
        while (maxExpectedMoveDuration > timer.MillisecondsRemaining / 10 - 200 && depth > 3)
        {
            depth--;
            // "/ 100" matches roughly my local machine in release mode and https://github.com/SebLague/Chess-Challenge/issues/381. Local debug mode would be about "/ 10".
            // Dynamic time control with averageOvershootFactor solves the problem of having different hardware
            maxExpectedMoveDuration = (int) (Math.Pow(pieceCountSquare, (depth - 2) / 1.5) / 100 * averageOvershootFactor);
        }
        
        // Search
        minimax(depth, board.IsWhiteToMove, -1000000000.0, 1000000000.0, true);
        overshootFactor[board.PlyCount / 2 % 4] = (double) (timer.MillisecondsElapsedThisTurn + 5) / (maxExpectedMoveDuration + 5); // Add 5ms to avoid 0ms rounds/predictions impacting too much
        //Console.WriteLine($"bestMoveEval={Math.Round(bestMoveEval)}, depth={depth}, expectedMs={maxExpectedMoveDuration}, actualMs={timer.MillisecondsElapsedThisTurn}, overshootMs={Math.Max(0, timer.MillisecondsElapsedThisTurn - maxExpectedMoveDuration)}, averageOvershootFactor={Math.Round(averageOvershootFactor, 2)}"); // #DEBUG

        return bestMove;
    }

    bool isHighPotentialMove(Move move)
    {
        board.MakeMove(move);
        var isInCheck = board.IsInCheck();
        board.UndoMove(move);
        return move.IsCapture || move.IsPromotion || move.IsCastles || isInCheck;
    }
    
    double minimax(int depth, bool whiteToMinimize, double alpha, double beta, bool assignBestMove)
    {
        Span<Move> moves = stackalloc Move[256];
        board.GetLegalMovesNonAlloc(ref moves);
        
        if (depth == 0 || board.IsInCheckmate() || board.IsDraw() || (moves.Length == 1 && assignBestMove)) // TODO 3 cases different?
        {
            if (assignBestMove)
            {
                bestMoveEval = evaluate();
                bestMove = moves[0];
            }
            return evaluate(); // Reminder: Don't cache if moves.Length == 1 && assignBestMove, this is just a shortcut
        }
            
        // Optimize ab-pruning: first check moves that are more likely to be good
        Span<int> movePotential = stackalloc int[moves.Length];
        int moveIndex = 0;
        foreach (var move in moves)
        {
            movePotential[moveIndex++] = isHighPotentialMove(move) ? -1 : 0;
        }
        movePotential.Sort(moves);

        if (whiteToMinimize)
        {
            var maxEval = Double.NegativeInfinity; // TODO extract function for both cases to spare code?
            foreach (var move in moves)
            {
                board.MakeMove(move);
                var eval = minimax(depth - 1, false, alpha, beta, false);
                board.UndoMove(move);
                alpha = Math.Max(alpha, eval);
                if (eval > maxEval)
                {
                    maxEval = eval;
                    if (assignBestMove)
                    {
                        bestMove = move;
                        bestMoveEval = eval;
                    }
                }

                if (beta <= alpha) break;
            }

            return maxEval;
        }
        else
        {
            var minEval = Double.PositiveInfinity;
            foreach (var move in moves)
            {
                board.MakeMove(move);
                var eval = minimax(depth - 1, true, alpha, beta, false);
                board.UndoMove(move);
                beta = Math.Min(beta, eval);
                if (eval < minEval)
                {
                    minEval = eval;
                    if (assignBestMove)
                    {
                        bestMove = move;
                        bestMoveEval = eval;
                    }
                }

                if (beta <= alpha) break;
            }

            return minEval;
        }
    }

    double evaluate()
    {
        return evaluate(true) - evaluate(false); // TODO strategy-evaluate (e.g. divide/multiply by how many plys played)
    }

    double evaluate(bool white)
    {
        // Checkmate is of course always best
        if (board.IsInCheckmate())
        {
            // Add/Subtract plyCount to prefer mate in fewer moves
            return board.IsWhiteToMove == white ? -100000000.0 + board.PlyCount : 100000000.0 - board.PlyCount;
        }

        if (board.IsDraw())
        {
            return 0;
        }

        var score = 0.0;

        foreach (var pieceList in board.GetAllPieceLists())
        {
            if (white != pieceList.IsWhitePieceList) continue;
            for (int pieceIndex = 0; pieceIndex < pieceList.Count; pieceIndex++)
            {
                var piece = pieceList[pieceIndex];
                score += pieceValues[(int)piece.PieceType];

                if (piece.IsPawn)
                {
                    // Make pawns move forward
                    var rank = piece.Square.Rank;
                    var ranksAwayFromPromotion = white ? rank : 7 - rank;
                    score += ranksAwayFromPromotion;
                } // TODO endgame evaluation: king in center vs side/top/bottom (or near other pieces, no matter of color): board weight + 1 center-weight

                if (piece.Square.Rank == (white ? 0 : 7))
                {
                    score += backRankPieceNegativeScore[(int)piece.PieceType];
                }
                
                var attacks =
                    BitboardHelper.GetPieceAttacks(piece.PieceType, piece.Square, board, pieceList.IsWhitePieceList);

                // Move pieces to places with much freedom TODO up to how much freedom is it relevant? bishop < 2 freedom = trapped = very bad
                // TODO freedom is more important, should lead to moving pawn forward after castling
                // TODO weight bei how "relevant" is attacking/protecting piece
                score += 0.5 * BitboardHelper.GetNumberOfSetBits(attacks);

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

        return score;
    }
}
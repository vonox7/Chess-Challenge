using System;
using System.Linq;
using ChessChallenge.API;

public class MyBot : IChessBot
{
    Board board;
    private Move bestMove;
    private double bestMoveEval;
    int[] pieceValues = { 0, 100, 300, 300, 500, 900, 10000 }; // TODO which values?
    private int[] backRankPieceNegativeScore = { 0, 0, -30, -25, 0, -20, 0 };
    int maxExpectedMoveDuration;
    private double[] overshootFactor = { 1, 1, 1, 1 };
    Move[,] killerMoves = new Move[1000, 2]; // Lets hope that we never have more than 1000 moves in a game
    
    // Use array of structs, because I could do that in less tokens than array of classes
    private Transposition[] transpositions = new Transposition[100000];
    struct Transposition
    {
        public ulong zobristKey;
        public Move bestMove; // TODO figure out if caching bestMove actually does something
        // TODO if adding more things like caching evaluation, also remember to check first the ply for which the eval was cached (?)
    }

    public Move Think(Board _board, Timer timer)
    {
        board = _board;
        bestMove = Move.NullMove;
        maxExpectedMoveDuration = 10000000;

        // Time control
        var depth = 8;
        // Add +2, as only with king on the board we still have 8 squares to move to, which is way above average for average pieces or average board density
        var pieceCountSquare = (BitboardHelper.GetNumberOfSetBits(board.BlackPiecesBitboard) + 2) * (BitboardHelper.GetNumberOfSetBits(board.WhitePiecesBitboard) + 2);
        var averageOvershootFactor = overshootFactor.Sum() / 4;
        while (maxExpectedMoveDuration > timer.MillisecondsRemaining / 10 - 200 && depth > 3)
        {
            depth--;
            // "/ 100" matches roughly my local machine in release mode and https://github.com/SebLague/Chess-Challenge/issues/381. Local debug mode would be about "/ 10".
            // Dynamic time control with averageOvershootFactor solves the problem of having different hardware
            maxExpectedMoveDuration = (int) (Math.Pow(pieceCountSquare, (depth - 2) / 1.5) / 100.0 * averageOvershootFactor);
        }
        
        // Search
        minimax(depth, board.IsWhiteToMove, -1000000000.0, 1000000000.0, true);
        overshootFactor[board.PlyCount / 2 % 4] = (double) (timer.MillisecondsElapsedThisTurn + 5) / (maxExpectedMoveDuration + 5); // Add 5ms to avoid 0ms rounds/predictions impacting too much
        //Console.WriteLine($"bestMoveEval={Math.Round(bestMoveEval)}, depth={depth}, expectedMs={maxExpectedMoveDuration}, actualMs={timer.MillisecondsElapsedThisTurn}, overshootMs={Math.Max(0, timer.MillisecondsElapsedThisTurn - maxExpectedMoveDuration)}, averageOvershootFactor={Math.Round(averageOvershootFactor, 2)}"); // #DEBUG

        return bestMove;
    }

    int getMovePotential(Move move)
    {
        // TODO figure out if 1000/-10/-1/-3/-1/-5 (and no scaling on capture-movePieceType) is a good guess
        var guess = 0;

        var transposition = transpositions[board.ZobristKey % 100000];
        if (transposition.zobristKey == board.ZobristKey && move == transposition.bestMove) guess += 1000; // TODO FIXME -1000
        
        // TODO check transposition table for previously good moves
        if (move == killerMoves[board.PlyCount, 0] || move == killerMoves[board.PlyCount, 1]) guess -= 10;

        board.MakeMove(move);
        if (board.IsInCheck()) guess -= 1;
        board.UndoMove(move);

        if (move.IsPromotion) guess -= 3;
        if (move.IsCastles) guess -= 1;
        // TODO why is this worse? if (move.IsCapture) guess -= 3;//(int)move.CapturePieceType - (int)move.MovePieceType + 5;
        if (move.IsCapture) guess -= 3;

        return guess;
    }
    
    double minimax(int depth, bool whiteToMinimize, double alpha, double beta, bool assignBestMove)
    {
        if (depth == 0 || board.IsInCheckmate() || board.IsDraw()) // TODO 3 cases different?
        {
            return evaluate();
        }

        var ply = board.PlyCount;

        Span<Move> moves = stackalloc Move[256];
        board.GetLegalMovesNonAlloc(ref moves);

        // Shortcut for when there is only one move available (only keep it when we have tokens left).
        // If we implement any caching, don't cache this case, because it is not a real evaluation.
        if (moves.Length == 1 && assignBestMove)
        {
            bestMoveEval = evaluate();
            bestMove = moves[0];
        }
            
        // Optimize via ab-pruning: first check moves that are more likely to be good
        Span<int> movePotential = stackalloc int[moves.Length];
        int moveIndex = 0;
        foreach (var move in moves)
        {
            movePotential[moveIndex++] = getMovePotential(move);
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
                    transpositions[board.ZobristKey % 100000] = new Transposition
                    {
                        zobristKey = board.ZobristKey,
                        bestMove = move
                    };
                    
                    maxEval = eval;
                    if (assignBestMove)
                    {
                        bestMove = move;
                        bestMoveEval = eval;
                    }
                }

                if (beta <= alpha)
                {
                    // By trial and error I figured out, that checking for promotion/castles/check doesn't help here
                    if (!move.IsCapture)
                    {
                        killerMoves[ply, 1] = killerMoves[ply, 0];
                        killerMoves[ply, 0] = move;
                    }
                    break;
                }
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
                    transpositions[board.ZobristKey % 100000] = new Transposition
                    {
                        zobristKey = board.ZobristKey,
                        bestMove = move
                    };
                    
                    minEval = eval;
                    if (assignBestMove)
                    {
                        bestMove = move;
                        bestMoveEval = eval;
                    }
                }

                if (beta <= alpha)
                {
                    // By trial and error I figured out, that checking for promotion/castles/check doesn't help here
                    if (!move.IsCapture)
                    {
                        killerMoves[ply, 1] = killerMoves[ply, 0];
                        killerMoves[ply, 0] = move;
                    }
                    break;
                }
            }

            return minEval;
        }
    }

    // TODO why are we making 1-move blunders in valibot-0.7? https://chess.stjo.dev/game/410390/
    double evaluate()
    {
        // Endgame evaluation: https://www.chessprogramming.org/Mop-up_Evaluation TODO reduce Tokens, this is quite a lot of code just to fix rook/queen endgame
        var whitePieceCount = BitboardHelper.GetNumberOfSetBits(board.WhitePiecesBitboard); 
        var blackPieceCount = BitboardHelper.GetNumberOfSetBits(board.BlackPiecesBitboard);
        var endgameScore = 0.0;
        // TODO don't jump to endgame evaluation all at once, but gradually shift to it
        if (whitePieceCount < 2 || blackPieceCount < 2)
        {
            // Endgame evaluation: https://www.chessprogramming.org/Mop-up_Evaluation
            var whiteIsLoosing = whitePieceCount < blackPieceCount;
            var loosingKingSquare = board.GetKingSquare(whiteIsLoosing);
            var winningKingSquare = board.GetKingSquare(!whiteIsLoosing);
            
            var centerDistanceOfLoosingKing = Math.Abs(loosingKingSquare.Rank - 3.5) + Math.Abs(loosingKingSquare.File - 3.5);
            var kingDistance = Math.Abs(loosingKingSquare.Rank - winningKingSquare.Rank) + Math.Abs(loosingKingSquare.File - winningKingSquare.File);
            // TODO 407/160 might be wrong (470 because centerDistanceOfLoosingKing is off by one, and whole scaling might be wrong when adding to our evaluate(bool) score)
            endgameScore = 470 * centerDistanceOfLoosingKing + 160 * (14 - kingDistance);
        }
        
        // Midgame evaluation: evaluate(true) - evaluate(false). But also needed for endgame to find actual mate.
        return evaluate(true) - evaluate(false) - endgameScore; // TODO strategy-evaluate (e.g. divide/multiply by how many plys played)
    }

    double evaluate(bool white)
    {
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
                // TODO wtf, we once promoted to a bishop?!? fix this
                score += pieceValues[(int)piece.PieceType];

                if (piece.IsPawn) // TODO should I check for passed pawn, is that with few tokens possible
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
        
        // Checkmate is of course always best. But a checkmate with a queen-promotion is considered best (because we might have overlooked an escape route that might have been possible with a rook-promotion)
        if (board.IsInCheckmate())
        {
            // Add/Subtract plyCount to prefer mate in fewer moves TODO the other way around? because in the above IsInCheck it is?!?
            score += board.IsWhiteToMove == white ? -100000000.0 + board.PlyCount : 100000000.0 - board.PlyCount;
        }

        return score;
    }
}
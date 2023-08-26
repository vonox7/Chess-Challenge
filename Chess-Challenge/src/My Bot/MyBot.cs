using System;
using ChessChallenge.API;

public class MyBot : IChessBot
{
    Board board;
    private Move bestMove;
    private double bestMoveEval;
    int[] pieceValues = { 0, 100, 300, 300, 500, 900, 10000 }; // TODO which values?
    private Transposition[] transpositions = new Transposition[1000000];
    int transpositionHit; // #DEBUG
    int transpositionMiss; // #DEBUG
    struct Transposition
    {
        public ulong zobristKey; // Store zobristKey to avoid hash collisions (not 100% perfect, but probably good enough)
        public Move bestMove; // TODO figure out if caching bestMove actually does something
        // TODO if adding more things like caching evaluation, also remember to check first the ply for which the eval was cached (?)
    }
    
    // See https://en.wikipedia.org/wiki/Alpha%E2%80%93beta_pruning#Heuristic_improvements
    // Lets hope that we never have more than 1000 moves in a game
    Move[] killerMoves = new Move[1000];

    public Move Think(Board _board, Timer timer)
    {
        board = _board;
        bestMove = Move.NullMove;
        transpositionHit = 0; // #DEBUG
        transpositionMiss = 0; // #DEBUG
        
        // Search via iterative deepening
        var depth = 0;
        // TODO figure out when to stop. Each additional depth-round takes ~5 times as much as the previous one.
        // So when assuming that we want to spend ~1/20th of the remaining time in the round, multiply by 5*20=100.
        while (timer.MillisecondsElapsedThisTurn * 100 < timer.MillisecondsRemaining)
        {
            minimax(++depth, board.IsWhiteToMove, -1000000000.0, 1000000000.0, true, false);
        }
        
        Console.WriteLine("bestMoveEval={0,10:F0}{1,13}, depth={2}, transpositionHits={3,4:F2}",  // #DEBUG
            bestMoveEval, // #DEBUG
            bestMoveEval > 100 ? " (white wins)" : (bestMoveEval < -100 ? " (black wins)" : ""), //#DEBUG
            depth, // #DEBUG
            (double) transpositionHit / (transpositionHit + transpositionMiss)); // #DEBUG
        
        return bestMove;
    }

    // TODO endgame: if we found mate, stop searching. With 2 queens on the board and mate in 1, we often search for > 1 second
    int getMovePotential(Move move)
    {
        // TODO figure out if 1000/-10/-1/-3/-1/-5 (and no scaling on capture-movePieceType) is a good guess
        var guess = 0;
        
        // Check transposition table for previously good moves
        var transposition = transpositions[board.ZobristKey % 1000000];
        if (transposition.zobristKey == board.ZobristKey)
        {
            transpositionHit++; // #DEBUG 
            // TODO checking for bestMove is the only thing we want to do here?
            if (move == transposition.bestMove) guess -= 1000;
        }
        else // #DEBUG 
        { // #DEBUG 
            transpositionMiss++; // #DEBUG 
        } // #DEBUG 

        if (move == killerMoves[board.PlyCount]) guess -= 10;

        // Queen promotions are best, but in edge cases knight promotions could also work.
        // But check also other promotions, as queen promotion might even lead to a stalemate (e.g. on 8/1Q4P1/3k4/8/3P2K1/P7/7P/8 w - - 3 53)
        if (move.IsPromotion) guess -= (int)move.PromotionPieceType;
        if (move.IsCastles) guess -= 1;
        if (move.IsCapture) guess -= (int)move.CapturePieceType - (int)move.MovePieceType + 5;

        return guess;
    }
    
    // Quiet: See https://en.wikipedia.org/wiki/Quiescence_search:
    // If last move was a capture, search following capture moves to see if it really was a good captures.
    double minimax(int depth, bool whiteToMinimize, double alpha, double beta, bool assignBestMove, bool quiet)
    {
        quiet = quiet && depth <= 0;
        if (quiet || depth <= -2 || board.IsInCheckmate() || board.IsDraw())
        {
            return evaluate();
        }

        var ply = board.PlyCount;

        Span<Move> moves = stackalloc Move[256];
        board.GetLegalMovesNonAlloc(ref moves, quiet);

        // Shortcut for when there is only one move available (only keep it when we have tokens left).
        // If we implement any caching, don't cache this case, because it is not a real evaluation.
        // Evaluation might be therefore pretty wrong
        if (moves.Length == 1 && assignBestMove)
        {
            bestMove = moves[0];
            bestMoveEval = evaluate(); // #DEBUG
            return bestMoveEval;
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
                var eval = minimax(depth - 1, false, alpha, beta, false, !move.IsCapture);
                board.UndoMove(move);
                alpha = Math.Max(alpha, eval);
                if (eval > maxEval)
                {
                    transpositions[board.ZobristKey % 1000000] = new Transposition
                    {
                        zobristKey = board.ZobristKey,
                        bestMove = move
                    };
                    maxEval = eval;
                    if (assignBestMove)
                    {
                        bestMove = move;
                        bestMoveEval = eval; // #DEBUG
                    }
                }

                if (beta <= alpha)
                {
                    // By trial and error I figured out, that checking for promotion/castles/check doesn't help here
                    if (!move.IsCapture)
                    {
                        killerMoves[ply] = move;
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
                var eval = minimax(depth - 1, true, alpha, beta, false, !move.IsCapture);
                board.UndoMove(move);
                beta = Math.Min(beta, eval);
                if (eval < minEval)
                {
                    transpositions[board.ZobristKey % 1000000] = new Transposition
                    {
                        zobristKey = board.ZobristKey,
                        bestMove = move
                    };
                    minEval = eval;
                    if (assignBestMove)
                    {
                        bestMove = move;
                        bestMoveEval = eval; // #DEBUG
                    }
                }

                if (beta <= alpha)
                {
                    if (!move.IsCapture)
                    {
                        killerMoves[ply] = move;
                    }
                    break;
                }
            }

            return minEval;
        }
    }

    double evaluate()
    {
        // Midgame evaluation: evaluate(true) - evaluate(false). But also needed for endgame to find actual mate.
        return evaluate(true) - evaluate(false); // TODO favour equal trades when we are in the lead
    }

    double evaluate(bool white)
    {
        if (board.IsDraw())
        {
            return 0;
        }

        var score = 0.0;
        //var undefendedPieces = white ? board.WhitePiecesBitboard : board.BlackPiecesBitboard;

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

                var attacks =
                    BitboardHelper.GetPieceAttacks(piece.PieceType, piece.Square, board, pieceList.IsWhitePieceList);
                //undefendedPieces &= ~attacks;

                // Move pieces to places with much freedom TODO up to how much freedom is it relevant? bishop < 2 freedom = trapped = very bad
                // TODO freedom is more important, should lead to moving pawn forward after castling
                // TODO weight bei how "relevant" is attacking/protecting piece
                // TODO try out this: score += Math.Log2(BitboardHelper.GetNumberOfSetBits(attacks));
                score += 0.5 * BitboardHelper.GetNumberOfSetBits(attacks);

                // TODO Pinning

                // Make pieces attacking/defending other pieces TODO same score for attack+defense?
                score += 1.5 * BitboardHelper.GetNumberOfSetBits(attacks & board.AllPiecesBitboard);
            }
        }

        // TODO try out make pieces protect other pieces: We want to have a position where all pieces are defended (didn't help in the current situation)
        // score -= 5 * BitboardHelper.GetNumberOfSetBits(undefendedPieces);

        // TODO favour early castle & castle rights

        // Putting someone in check is quite often good
        /*if (board.IsInCheck())
        {
            // TODO why is this +/-, and the other one below for IsInCheckmate() is -/+?
            score += board.IsWhiteToMove == white ? 70 : -70;
        }*/
        
        // Checkmate is of course always best. But a checkmate with a queen-promotion is considered best (because we might have overlooked an escape route that might have been possible with a rook-promotion)
        if (board.IsInCheckmate())
        {
            // Add/Subtract plyCount to prefer mate in fewer moves. Multiply by more than any e.g. pawn->queen promotion while taking opponent queen would bring
            var mateInXboost = board.PlyCount * 10000;
            score += board.IsWhiteToMove == white ? -100000000.0 + mateInXboost : 100000000.0 - mateInXboost;
        }

        return score;
    }
}
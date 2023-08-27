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
        public Move bestMove; // TODO Do we want to store not only the single best move, but the best 2-3 moves?
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
        while (timer.MillisecondsElapsedThisTurn * 200 < timer.MillisecondsRemaining)
        {
            if (Double.IsNaN(minimax(++depth, board.IsWhiteToMove, -1000000000.0, 1000000000.0, true, false))) break;
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
            bestMoveEval = Double.NaN; // #DEBUG
            return Double.NaN;
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
                // Capturing the queen or getting a check is quite often so unstable, that we need to check 1 more move deep
                var eval = minimax(depth - ((move.IsCapture && move.CapturePieceType == PieceType.Queen) || board.IsInCheck() ? 0 : 1),
                    false, alpha, beta, false, !move.IsCapture);
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
                // Capturing the queen or getting a check is quite often so unstable, that we need to check 1 more move deep
                var eval = minimax(depth - ((move.IsCapture && move.CapturePieceType == PieceType.Queen) || board.IsInCheck() ? 0 : 1),
                    true, alpha, beta, false, !move.IsCapture);
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
        if (board.IsDraw())
        {
            return 0;
        }

        var score = 0.0;
        var whitePieceCount = 0;
        var blackPieceCount = 0;
        
        // Midgame evaluation (but also needed for endgame to find actual mate)
        foreach (var pieceList in board.GetAllPieceLists())
        {
            for (int pieceIndex = 0; pieceIndex < pieceList.Count; pieceIndex++)
            {
                var piece = pieceList[pieceIndex];
                if (pieceList.IsWhitePieceList) whitePieceCount++; else blackPieceCount++;
                var whitePieceMultiplier = pieceList.IsWhitePieceList ? 1 : -1;
                score += pieceValues[(int)piece.PieceType] * whitePieceMultiplier;

                if (piece.IsPawn)
                {
                    // Make pawns move forward
                    var rank = piece.Square.Rank;
                    var ranksAwayFromPromotion = pieceList.IsWhitePieceList ? rank : 7 - rank;
                    score += ranksAwayFromPromotion * whitePieceMultiplier;
                } // TODO endgame evaluation: king in center vs side/top/bottom (or near other pieces, no matter of color): board weight + 1 center-weight

                var attacks =
                    BitboardHelper.GetPieceAttacks(piece.PieceType, piece.Square, board, pieceList.IsWhitePieceList);

                // Move pieces to places with much freedom TODO up to how much freedom is it relevant? bishop < 2 freedom = trapped = very bad
                // TODO freedom is more important, should lead to moving pawn forward after castling
                // TODO weight bei how "relevant" is attacking/protecting piece
                // TODO try out this: score += Math.Log2(BitboardHelper.GetNumberOfSetBits(attacks));
                score += 0.5 * BitboardHelper.GetNumberOfSetBits(attacks) * whitePieceMultiplier;

                // TODO Pinning

                // Make pieces attacking/defending other pieces TODO same score for attack+defense?
                score += 1.5 * BitboardHelper.GetNumberOfSetBits(attacks & board.AllPiecesBitboard) * whitePieceMultiplier;
            }
        }
        
        // Checkmate is of course always best. But a checkmate with a queen-promotion is considered best (because we might have overlooked an escape route that might have been possible with a rook-promotion)
        var whiteBoardMultiplier = board.IsWhiteToMove ? -1 : 1;
        if (board.IsInCheckmate())
        {
            // Add/Subtract plyCount to prefer mate in fewer moves. Multiply by more than any e.g. pawn->queen promotion while taking opponent queen would bring
            score += whiteBoardMultiplier * (100000000.0 - board.PlyCount * 10000);
        }
        
        // Endgame evaluation: https://www.chessprogramming.org/Mop-up_Evaluation TODO reduce Tokens, this is quite a lot of code just to fix rook/queen endgame
        // TODO don't jump to endgame evaluation all at once, but gradually shift to it (so slight boost when we have 2 pieces left)
        if (whitePieceCount < 2 || blackPieceCount < 2)
        {
            // Endgame evaluation: https://www.chessprogramming.org/Mop-up_Evaluation
            var whiteIsLoosing = whitePieceCount < blackPieceCount;
            var loosingKingSquare = board.GetKingSquare(whiteIsLoosing);
            var winningKingSquare = board.GetKingSquare(!whiteIsLoosing);
            
            var centerDistanceOfLoosingKing = Math.Abs(loosingKingSquare.Rank - 3.5) + Math.Abs(loosingKingSquare.File - 3.5);
            var kingDistance = Math.Abs(loosingKingSquare.Rank - winningKingSquare.Rank) + Math.Abs(loosingKingSquare.File - winningKingSquare.File);
            // TODO 407/160 might be wrong (470 because centerDistanceOfLoosingKing is off by one, and whole scaling might be wrong when adding to our evaluate(bool) score)
            score += whiteBoardMultiplier * (470 * centerDistanceOfLoosingKing + 160 * (14 - kingDistance));
        }
        
        return score;
    }
}
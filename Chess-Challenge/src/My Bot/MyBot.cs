using System;
using ChessChallenge.API;

public class MyBot : IChessBot
{
    Board board;
    Timer timer;
    bool cancel;
    Move bestMove;
    double bestMoveEval;
    int[] pieceValues = { 0, 100, 300, 300, 500, 900, 10000 }; // TODO which values?
    // 15MB * 16 bytes = 240MB, below the 256MB limit, checked via Marshal.SizeOf<Transposition>()
    Transposition[] transpositions = new Transposition[15_000_000];
    int transpositionHit; // #DEBUG
    int transpositionMiss; // #DEBUG
    struct Transposition
    {
        public ulong zobristKey; // 8 byte: Store zobristKey to avoid hash collisions (not 100% perfect, but probably good enough)
        public ushort bestMoveRawValue; // 2 bytes
        public sbyte flag, depth; // 2 x 1 byte
        public float eval; // 4 bytes
    }
    long totalMovesSearched; // #DEBUG
    long toalSearchEndNodes; // #DEBUG
    
    // See https://en.wikipedia.org/wiki/Alpha%E2%80%93beta_pruning#Heuristic_improvements
    // Lets hope that we never have more than 1000 moves in a game
    Move[] killerMoves = new Move[1000];

    public Move Think(Board _board, Timer _timer)
    {
        board = _board;
        timer = _timer;
        bestMove = Move.NullMove;
        cancel = false;
        var prevBestMove = bestMove;
        transpositionHit = 0; // #DEBUG
        transpositionMiss = 0; // #DEBUG

        // Search via iterative deepening
        var depth = 0;
        // TODO figure out when to stop. Each additional depth-round takes ~5 times as much as the previous one.
        // So when assuming that we want to spend ~1/20th of the remaining time in the round, multiply by 5*20=100.
        // Max 25 depth, so we don't end up in a loop on forced checkmate. Also 5*25=125, and sbyte can hold up to 127.
        while (timer.MillisecondsElapsedThisTurn * 200 < timer.MillisecondsRemaining && depth < 25)
        {
            // 1 depth is represented as 5*depth, so we can also do smaller depth-steps on critical positions
            if (Double.IsNaN(minimax(5 * ++depth, -1000000000.0, 1000000000.0, true))) break;
            prevBestMove = bestMove;
        }

        if (cancel) bestMove = prevBestMove;

        bestMoveEval *= board.IsWhiteToMove ? 1 : -1; // #DEBUG
        Console.WriteLine(
            "{0,2} bestMoveEval={1,10:F0}{2,13}, depth={3}, transpositionHits={4,4:F2}, traversed={5}, evaluated={6}", // #DEBUG
            board.PlyCount / 2 + 1, // #DEBUG
            bestMoveEval, // #DEBUG
            bestMoveEval > 100 ? " (white wins)" : (bestMoveEval < -100 ? " (black wins)" : ""), //#DEBUG
            depth, // #DEBUG
            (double) transpositionHit / (transpositionHit + transpositionMiss),
            (totalMovesSearched - toalSearchEndNodes) / 1000 / 1000.0 + "M", // #DEBUG
            toalSearchEndNodes / 1000 / 1000.0 + "M"); // #DEBUG
        
        return bestMove;
    }

    // TODO endgame: if we found mate, stop searching. With 2 queens on the board and mate in 1, we often search for > 1 second
    int getMovePotential(Move move)
    {
        // TODO figure out if 1000/-10/-1/-3/-1/-5 (and no scaling on capture-movePieceType) is a good guess
        var guess = 0;
        
        // Check transposition table for previously good moves
        var transposition = transpositions[board.ZobristKey % 15_000_000];
        if (transposition.zobristKey == board.ZobristKey)
        {
            transpositionHit++; // #DEBUG 
            // TODO checking for bestMove is the only thing we want to do here?
            if (move.RawValue == transposition.bestMoveRawValue) guess -= 1000;
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
    
    double minimax(int depth, double alpha, double beta, bool assignBestMove, bool allowNull = true)
    {
        if (timer.MillisecondsElapsedThisTurn * 20 > timer.MillisecondsRemaining) cancel = true;
        if (cancel) return Double.NaN;
        double bestEval = -1000000000 - depth;
        totalMovesSearched++; // #DEBUG
        
        if (board.IsDraw())
        {
            toalSearchEndNodes++; // #DEBUG
            return 0;
        }

        ref var transposition = ref transpositions[board.ZobristKey % 15_000_000];
        if (!assignBestMove && transposition.depth >= depth && transposition.zobristKey == board.ZobristKey)
        {
            // TODO is all this where we set the flag really correct? see https://web.archive.org/web/20071031100051/http://www.brucemo.com/compchess/programming/hashing.htm
            if (transposition.flag == 0) return transposition.eval; // EXACT
            if (transposition.flag == 1 && transposition.eval <= alpha) return alpha; // ALPHA
            if (transposition.flag == 2 && transposition.eval >= beta) return beta; // BETA
        }
        var eval = evaluate();
        if (depth <= -100) return eval; // Don't over-evaluate certain positions (this also avoids underflow of sbyte)
        
        // Null move pruning
        if (depth >= 3 && allowNull && eval >= beta && board.TrySkipTurn())
        {
            double nullMoveEval = -minimax(depth - 15, -beta, -beta + 1, false, false);
            board.UndoSkipTurn();
            if (nullMoveEval >= beta) return nullMoveEval;
        }
        
        if (depth <= 0)
        {
            bestEval = eval;
            if (bestEval >= beta) {
                toalSearchEndNodes++; // #DEBUG
                
                transposition.zobristKey = board.ZobristKey;
                transposition.eval = (float) bestEval;
                transposition.depth = (sbyte) depth;
                transposition.flag = 0;
                // We don't set the bestMove here. Keep it the way it was because it might not be bad (TODO or reset to NullMove)
                
                return bestEval; // eval seems to be quiet, so stop here
            }
            alpha = Math.Max(alpha, bestEval);
        }
        var ply = board.PlyCount;

        Span<Move> moves = stackalloc Move[256];
        board.GetLegalMovesNonAlloc(ref moves, depth <= 0);

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

        foreach (var move in moves)
        {
            board.MakeMove(move);
            // Extension: Getting a check is quite often so unstable, that we need to check 1 more move deep (but not forever, so otherwise reduce by 0.2)
            eval = -minimax(depth - (board.IsInCheck() ? 1 : 5), -beta, -alpha, false);
            board.UndoMove(move);
            if (cancel) return Double.NaN;
            alpha = Math.Max(alpha, eval);
            
            if (eval > bestEval)
            {
                bestEval = eval;
                
                // Update transposition as early as possible, to let it find on subsequent searches
                transposition.zobristKey = board.ZobristKey;
                transposition.eval = (float) bestEval;
                transposition.depth = (sbyte) depth;
                transposition.bestMoveRawValue = move.RawValue;
                transposition.flag = 1;
                
                if (assignBestMove)
                {
                    bestMove = move;
                    bestMoveEval = eval; // #DEBUG
                }
                
                alpha = Math.Max(alpha, bestEval);
                if (beta <= alpha)
                {
                    transposition.flag = 2;
                    // By trial and error I figured out, that checking for promotion/castles/check doesn't help here
                    if (!move.IsCapture)
                    {
                        killerMoves[ply] = move;
                    }
                    break;
                }
            }
        }

        return bestEval;
    }
     double evaluate()
    {
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

        // 40: Trade on equal material // TODO which value? also on the divisor only 38 because of 2 kings always being here?
        return 40 * score / (40 + whitePieceCount + blackPieceCount) * -whiteBoardMultiplier;
    }
}
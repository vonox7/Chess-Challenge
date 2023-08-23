using System;
using System.Linq;
using ChessChallenge.API;

public class MyBot : IChessBot
{
    Board board;
    private Move bestMove;
    private double bestMoveEval; // #DEBUG
    int[] pieceValues = { 0, 100, 300, 300, 500, 900, 10000 }; // TODO which values?
    int maxExpectedMoveDuration;
    private double[] overshootFactor = { 1, 1, 1, 1 };
    Move[,] killerMoves = new Move[1000, 2]; // Lets hope that we never have more than 1000 moves in a game
    
    // Use array of structs, because I could do that in less tokens than array of classes
    /*private Transposition[] transpositions = new Transposition[100000];
    struct Transposition
    {
        public ulong zobristKey;
        public Move bestMove; // TODO figure out if caching bestMove actually does something
        // TODO if adding more things like caching evaluation, also remember to check first the ply for which the eval was cached (?)
    }*/

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
            maxExpectedMoveDuration = (int) (Math.Pow(pieceCountSquare, (depth - 2) / 2.2) / 100.0 * averageOvershootFactor);
        }
        
        // Search
        minimax(depth, board.IsWhiteToMove, -1000000000.0, 1000000000.0, true);
        overshootFactor[board.PlyCount / 2 % 4] = (double) (timer.MillisecondsElapsedThisTurn + 5) / (maxExpectedMoveDuration + 5); // Add 5ms to avoid 0ms rounds/predictions impacting too much
        Console.WriteLine("bestMoveEval={0,10:F0}{1,13}, depth={2}, expectedMs={3,6}, actualMs={4,6}, overshootMs={5,4}, avgOvershootFactor={6,4:F2}",  // #DEBUG
            bestMoveEval, // #DEBUG
            bestMoveEval > 100 ? " (white wins)" : (bestMoveEval < 100 ? " (black wins)" : ""), //#DEBUG
            depth, // #DEBUG
            maxExpectedMoveDuration, // #DEBUG
            timer.MillisecondsElapsedThisTurn, // #DEBUG
            Math.Max(0, timer.MillisecondsElapsedThisTurn - maxExpectedMoveDuration), // #DEBUG
            averageOvershootFactor); // #DEBUG

        // TODO wtf, we sometimes promote to a bishop or rook?!? fix this
        /*if (bestMove.IsPromotion && (bestMove.PromotionPieceType == PieceType.Bishop || // #DEBUG
                                     bestMove.PromotionPieceType == PieceType.Rook)) // #DEBUG
        { // #DEBUG
            Console.WriteLine("I am " + (board.IsWhiteToMove ? "white" : "black")); // #DEBUG
            Console.WriteLine("---"); // #DEBUG
            for (int i = board.PlyCount; i < board.PlyCount + 10; i++) // #DEBUG
            { // #DEBUG
                Console.WriteLine(killerMoves[i, 0] + " " + killerMoves[i, 1]); // #DEBUG
            } // #DEBUG
            Console.WriteLine("---"); // #DEBUG
            Console.WriteLine("now -> " + evaluate()); // #DEBUG
            board.MakeMove(bestMove); // #DEBUG
            Console.WriteLine((bestMove.PromotionPieceType == PieceType.Bishop ? "bishop" : "rook") + " -> " + evaluate()); // #DEBUG
            board.UndoMove(bestMove); // #DEBUG
            var queenPromotionMove = new Move(bestMove.ToString().Substring("Move: '".Length, 4) + "q", board); // #DEBUG
            board.MakeMove(queenPromotionMove); // #DEBUG
            Console.WriteLine("queen -> " + evaluate()); // #DEBUG
            board.UndoMove(queenPromotionMove); // #DEBUG
            Console.WriteLine("---"); // #DEBUG
            for (int i = 1; i <= 7; i++) // #DEBUG
            { // #DEBUG
                minimax(i, board.IsWhiteToMove, -1000000000.0, 1000000000.0, true); // #DEBUG
                Console.WriteLine("Best move depth={0}: {1}", i, bestMove); // #DEBUG
            } // #DEBUG
            for (int i = 1; i <= 7; i++) // #DEBUG
            { // #DEBUG
                minimax(i, board.IsWhiteToMove, -1000000000.0, 1000000000.0, true); // #DEBUG
                Console.WriteLine("Best move depth={0}: {1}", i, bestMove); // #DEBUG
            } // #DEBUG
            Console.WriteLine("---"); // #DEBUG
            for (int i = board.PlyCount; i < board.PlyCount + 10; i++) // #DEBUG
            { // #DEBUG
                Console.WriteLine(killerMoves[i, 0] + " " + killerMoves[i, 1]); // #DEBUG
            } // #DEBUG
            minimax(1, board.IsWhiteToMove, -1000000000.0, 1000000000.0, true); // #DEBUG
            Console.WriteLine($"Best move depth=1: {bestMove}"); // #DEBUG
            Console.WriteLine(board.CreateDiagram()); // #DEBUG
            //throw new Exception("WTF, again a bishop/rook promotion?!?"); // #DEBUG
        }*/ // #DEBUG


        // TODO sometimes we get FiftyMoveRule, but still had an eval of e.g. -3300 (should have been 0)
        // TODO eval can drastically jump (e.g. from -300 to 2000 when changing from depth 5 to 6)
        return bestMove;
    }

    int getMovePotential(Move move)
    {
        // TODO figure out if 1000/-10/-1/-3/-1/-5 (and no scaling on capture-movePieceType) is a good guess
        var guess = 0;

        //var transposition = transpositions[board.ZobristKey % 100000]; // TODO wtf, why is this bad, and +1000 would be still good? 
        //if (transposition.zobristKey == board.ZobristKey && move == transposition.bestMove) guess -= 1000;
        
        // TODO check transposition table for previously good moves
        if (move == killerMoves[board.PlyCount, 0] || move == killerMoves[board.PlyCount, 1]) guess -= 10;

        board.MakeMove(move);
        if (board.IsInCheck()) guess -= 1;
        board.UndoMove(move);

        if (move.IsPromotion) guess -= 3;
        if (move.IsCastles) guess -= 1;
        if (move.IsCapture) guess -= (int)move.CapturePieceType - (int)move.MovePieceType + 5;

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
                var eval = minimax(depth - 1, false, alpha, beta, false);
                board.UndoMove(move);
                alpha = Math.Max(alpha, eval);
                if (eval > maxEval)
                {
                    /*transpositions[board.ZobristKey % 100000] = new Transposition
                    {
                        zobristKey = board.ZobristKey,
                        bestMove = move
                    };*/
                    
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
                    /*transpositions[board.ZobristKey % 100000] = new Transposition
                    {
                        zobristKey = board.ZobristKey,
                        bestMove = move
                    };*/
                    
                    minEval = eval;
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
        return evaluate(true) - evaluate(false) - endgameScore * (board.IsWhiteToMove ? 1 : -1); // TODO strategy-evaluate (e.g. divide/multiply by how many plys played)
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
                score += pieceValues[(int)piece.PieceType];

                if (piece.IsPawn) // TODO should I check for passed pawn, is that with few tokens possible
                {
                    // Make pawns move forward
                    var rank = piece.Square.Rank;
                    var ranksAwayFromPromotion = white ? rank : 7 - rank;
                    score += ranksAwayFromPromotion;
                } // TODO endgame evaluation: king in center vs side/top/bottom (or near other pieces, no matter of color): board weight + 1 center-weight

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
        /*if (board.IsInCheck())
        {
            // TODO why is this +/-, and the other one below for IsInCheckmate() is -/+?
            score += board.IsWhiteToMove == white ? 70 : -70;
        }*/
        
        // Checkmate is of course always best. But a checkmate with a queen-promotion is considered best (because we might have overlooked an escape route that might have been possible with a rook-promotion)
        if (board.IsInCheckmate())
        {
            // Add/Subtract plyCount to prefer mate in fewer moves
            score += board.IsWhiteToMove == white ? -100000000.0 + board.PlyCount : 100000000.0 - board.PlyCount;
        }

        return score;
    }
}
# Chess Bot
This is a chess bot implementation of the [Chess-Challenge project by SebLague](https://github.com/SebLague/Chess-Challenge).
The goal of the competition is to create a small chess bot (in C#) using a provided chess framework.
chess.com evaluates the performance of this bot while playing against itself with an 
1250 ELO rating using 1 minute of thinking time per game and bot.

## Bot Brain Capacity
Currently this chess bot uses 952/1024 C# code tokens. Due to the goal of token minimization, some of the code is not very readable (e.g. all functions & variables are inlined wherever possible).

## Search
* Iterative deepening
 * Time management: Checked after each iteration to not start a new iteration when the time is almost up to not start a new search
 * Time management: Check during search if time is really up
* Negamax search
 * With alpha-beta pruning
 * With quiescence search
 * Using stackalloc to avoid heap allocations
* Transposition table
 * Cache: best move, evaluation, depth, evaluation-flag (exact / alpha-cutoff / beta-cutoff)
 * Memory Optimized: One transposition table entry is only 16 bytes, so the bot can use 15 million transposition entries to be below the 256MB memory limit.
* Move ordering heuristic
 * Best move from transposition table
 * Killer moves
 * Promotions
 * Castles
 * Captures (MVV/LVA)
 * History heuristics
* Null move pruning (but not during endgames, there I figured out that NMP might skip a mate)
* Single move optimization: If there is only one move, don't search
* Search extension: When in check, search one ply deeper (with limit to avoid infinite recursion)

## Evaluation
I decided to write a custom evaluation function to not use the [quite common approach in the competition](https://github.com/SebLague/Chess-Challenge/forks) which uses compressed presto tables.
The evaluation function is based on the following features:
 * Pawn/piece values
 * Pawn position: 1 centipawn per square moved forward
 * Pawn/piece freedom: 0.5 centipawns per possible moves
 * Pawn/piece attacks: 1.5 centipawns per possible enemy attacks
 * Pawn/piece defense: 1.5 centipawns per defended own pawn/piece
 * Checkmate: Prefer checkmates in fewer moves
 * Trade equal material when ahead in material
 * Endgame evaluation: When one side only has 1 king left, a mop-up evaluation with center manhattan-distance is added to force the king in a queen/rook endgame to the side.
This leads to the emergent behaviour of:
 * Reasonable pawn structure, as pawns want to defend themselves
 * Opening: (almost) reasonable piece development, as pieces want to "see" the board
 * Knights: Prefer center squares, as they have there more freedom/attacks/defenses
 * King: After castling, the king prefers to stay behind 3 pawns (because there he defends the pawns and still has some freedom). However, when the king didn't castle, he often moves 1 square forward to get more freedom and to defend developed pawns/pieces. I think, this not good behaviour can not be easily fixed by using only a few code tokens.
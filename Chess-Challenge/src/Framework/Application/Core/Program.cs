using Raylib_cs;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace ChessChallenge.Application
{
    static class Program
    {
        const bool hideRaylibLogs = true;
        static Camera2D cam;

        public static void Main()
        {
            Vector2 loadedWindowSize = GetSavedWindowSize();
            int screenWidth = (int)loadedWindowSize.X;
            int screenHeight = (int)loadedWindowSize.Y;

            if (hideRaylibLogs)
            {
                unsafe
                {
                    Raylib.SetTraceLogCallback(&LogCustom);
                }
            }

            Raylib.InitWindow(screenWidth, screenHeight, "Chess Coding Challenge");
            Raylib.SetTargetFPS(60);

            UpdateCamera(screenWidth, screenHeight);

            ChallengeController controller = new();
            
            // TODO controller.StartNewBotMatch(ChallengeController.PlayerType.MyBot, ChallengeController.PlayerType.EvilBot);

            while (!Raylib.WindowShouldClose())
            {
                Raylib.BeginDrawing();
                Raylib.ClearBackground(new Color(22, 22, 22, 255));
                Raylib.BeginMode2D(cam);

                controller.Update();
                controller.Draw();

                Raylib.EndMode2D();

                controller.DrawOverlay();

                Raylib.EndDrawing();
            }

            Raylib.CloseWindow();

            controller.Release();
            UIHelper.Release();
        }

        public static void SetWindowSize(Vector2 size)
        {
            Raylib.SetWindowSize((int)size.X, (int)size.Y);
            UpdateCamera((int)size.X, (int)size.Y);
            SaveWindowSize();
        }

        public static Vector2 ScreenToWorldPos(Vector2 screenPos) => Raylib.GetScreenToWorld2D(screenPos, cam);

        static void UpdateCamera(int screenWidth, int screenHeight)
        {
            cam = new Camera2D();
            cam.target = new Vector2(0, 15);
            cam.offset = new Vector2(screenWidth / 2f, screenHeight / 2f);
            cam.zoom = screenWidth / 1280f * 0.7f;
        }


        [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        private static unsafe void LogCustom(int logLevel, sbyte* text, sbyte* args)
        {
        }

        static Vector2 GetSavedWindowSize()
        {
            if (File.Exists(FileHelper.PrefsFilePath))
            {
                string prefs = File.ReadAllText(FileHelper.PrefsFilePath);
                if (!string.IsNullOrEmpty(prefs))
                {
                    if (prefs[0] == '0')
                    {
                        return Settings.ScreenSizeSmall;
                    }
                    else if (prefs[0] == '1')
                    {
                        return Settings.ScreenSizeBig;
                    }
                }
            }
            return Settings.ScreenSizeSmall;
        }

        static void SaveWindowSize()
        {
            Directory.CreateDirectory(FileHelper.AppDataPath);
            bool isBigWindow = Raylib.GetScreenWidth() > Settings.ScreenSizeSmall.X;
            File.WriteAllText(FileHelper.PrefsFilePath, isBigWindow ? "1" : "0");
        }

      

    }


}



/* TODO
How often are pieces/pawns defended / how many pieces/pawns get defended by own pieces/pawns?
How many moves does oneself/opponent has in current position? <3 or so should cause problems (the less the better, maybe even quadratic?)
Trade equal when up in material
For easier training of all weights/scores: play against stockfish and/or use stockfish eval function as comparison?
Train evaluation on whole games (against self, stockfish various levels) and also find a list of "interesting posotions" (there is 1 file of them in the repo?)
Training: evaluation + next move (and how does stockfish like it)
Eval: remaining time (vs opponent remaining time)
Rook wants to be aligned on king file: maybe this is implicit by "how many opponents am I attacking, even though pieces in the middle - weight how much own+opponent pieces/pawns are worth. On own consider to how many places they can move)
Move search: look a moves in advance (remainingTime/500), take fist b moves that give best evaluation, look again c moves in advance. Do that cycle d times.
Maybe b should not be fixed, but in a range that depends on the evaluation difference
Time control: if there is enough eval benefit, stop early
Disregard all moves that don't bring relevant benefits
Passed pawns: high value
Lower weight pawn moves of all but center pawns in first x moves
But do not bring out queen early, even if it would have more movement
Attacks on pieces/pawns near the king are more worth
Store good lines, opponent probably plays one of them, then we already can start digging deeper
 */
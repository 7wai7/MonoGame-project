using MonoGame_project.Configuration;
using MonoGame_project.Engine.Diagnostics;

if (args.Contains("--physics-smoke", StringComparer.Ordinal))
{
    MonoGame_project.Engine.Physics.PhysicsSmokeTest.Run();
    return;
}

StartupProfiler.Start();
if (OperatingSystem.IsMacOS())
{
    // MonoGame reads this when creating the SDL window.
    Environment.SetEnvironmentVariable("MONOGAME_ENABLE_HIGHDPI",
        GameSettings.Default.UseHighDpi ? "1" : "0");
}

using var game = new MonoGame_project.MainGame();
game.Run();

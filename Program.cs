using MonoGame_project.Engine.Diagnostics;

if (args.Contains("--physics-smoke", StringComparer.Ordinal))
{
    MonoGame_project.Engine.Physics.PhysicsSmokeTest.Run();
    return;
}

StartupProfiler.Start();
using var game = new MonoGame_project.MainGame();
game.Run();

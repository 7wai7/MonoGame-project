using System;
using Microsoft.Xna.Framework;

namespace MonoGame_project.Configuration;

/// <summary>
/// Platform and main-loop settings kept outside game logic.
/// </summary>
public sealed record GameSettings
{
    public static GameSettings Default { get; } = new();

    public string WindowTitle { get; init; } = "MonoGame Project";
    public int BackBufferWidth { get; init; } = 1280;
    public int BackBufferHeight { get; init; } = 720;
    public bool VSync { get; init; } = true;
    public Color ClearColor { get; init; } = new(22, 24, 31);

    /// <summary>Use native Retina backbuffer resolution on macOS.</summary>
    public bool UseHighDpi { get; init; } = true;

    /// <summary>Scale of the whole Gum UI in logical window coordinates.</summary>
    public float UiScale { get; init; } = 0.75f;

    /// <summary>Simulation frequency. Rendering remains variable-rate.</summary>
    public TimeSpan FixedTimeStep { get; init; } = TimeSpan.FromSeconds(1.0 / 60.0);

    /// <summary>Protects the simulation from a spiral of death after a long stall.</summary>
    public TimeSpan MaximumFrameTime { get; init; } = TimeSpan.FromMilliseconds(250);
}

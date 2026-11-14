namespace MonoGame_project.Engine.Diagnostics;

public enum BenchmarkRenderMode { ClearOnly, FlatTerrain, CurrentTerrain }

/// <summary>Command-line settings for an isolated, automatically completed run.</summary>
public sealed record PerformanceBenchmarkOptions(
    bool UiEnabled,
    bool Fullscreen,
    int Seconds,
    int Rounds,
    string OutputRoot,
    int InternalWidth = 0,
    int InternalHeight = 0)
{
    public bool UsesFixedRenderTarget => InternalWidth > 0;
    public float RenderAspectRatio => UsesFixedRenderTarget
        ? (float)InternalWidth / InternalHeight : 0f;

    public static PerformanceBenchmarkOptions? Parse(string[] args)
    {
        if (!args.Contains("--performance-benchmark", StringComparer.Ordinal)) return null;

        string Value(string prefix, string fallback) =>
            args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.Ordinal))
                ?[prefix.Length..] ?? fallback;

        var ui = Value("--benchmark-ui=", "on");
        var display = Value("--benchmark-display=", "window");
        if (ui is not ("on" or "off") || display is not ("window" or "fullscreen"))
            throw new ArgumentException("Use --benchmark-ui=on|off and --benchmark-display=window|fullscreen.");
        if (!int.TryParse(Value("--benchmark-seconds=", "10"), out var seconds) || seconds < 2 || seconds > 120 ||
            !int.TryParse(Value("--benchmark-rounds=", "2"), out var rounds) || rounds < 1 || rounds > 10)
            throw new ArgumentException("Benchmark seconds must be 2..120 and rounds 1..10.");

        if (!int.TryParse(Value("--benchmark-internal-width=", "0"), out var internalWidth) ||
            !int.TryParse(Value("--benchmark-internal-height=", "0"), out var internalHeight) ||
            ((internalWidth == 0) != (internalHeight == 0)) ||
            (internalWidth != 0 && (internalWidth < 320 || internalHeight < 240 ||
                                    internalWidth > 8192 || internalHeight > 8192)))
            throw new ArgumentException("Internal benchmark resolution must be 0/0 or 320..8192 x 240..8192.");
        if (internalWidth != 0 && ui != "off")
            throw new ArgumentException("Fixed internal render resolution is supported only with UI off.");

        return new PerformanceBenchmarkOptions(ui == "on", display == "fullscreen", seconds,
            rounds, Value("--benchmark-output=", Path.Combine(Directory.GetCurrentDirectory(), "BenchmarkResults")),
            internalWidth, internalHeight);
    }
}

namespace MonoGame_project.Engine.Input;

/// <summary>Built-in input contexts shared by current engine features.</summary>
public static class InputContexts
{
    public static InputContext InspectViewport { get; } = new(
        nameof(InspectViewport),
        MouseMode.Relative);

    /// <summary>Held Left Ctrl releases the inspect cursor without blocking movement.</summary>
    public static InputContext InspectCursor { get; } = new(
        nameof(InspectCursor),
        MouseMode.Free,
        blocksLowerInput: false);

    public static InputContext PauseMenu { get; } = new(
        nameof(PauseMenu),
        MouseMode.Free);

    public static InputContext Settings { get; } = new(
        nameof(Settings),
        MouseMode.Free);

    /// <summary>Free cursor and no inspect input during automated camera measurements.</summary>
    public static InputContext TerrainBenchmark { get; } = new(
        nameof(TerrainBenchmark),
        MouseMode.Free);

    public static bool CanUseInspect(InputContextStack stack) =>
        stack.CanReceiveInput(InspectViewport) ||
        stack.CanReceiveInput(InspectCursor);
}

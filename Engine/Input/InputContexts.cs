namespace MonoGame_project.Engine.Input;

/// <summary>Built-in input contexts shared by current engine features.</summary>
public static class InputContexts
{
    public static InputContext InspectViewport { get; } = new(
        nameof(InspectViewport),
        MouseMode.Relative);

    public static InputContext PauseMenu { get; } = new(
        nameof(PauseMenu),
        MouseMode.Free);
}

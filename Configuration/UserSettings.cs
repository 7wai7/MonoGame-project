namespace MonoGame_project.Configuration;

/// <summary>Settings chosen by the player and stored outside the game files.</summary>
public sealed record UserSettings
{
    public static UserSettings Default { get; } = new();

    public bool Fullscreen { get; init; }
    public bool VSync { get; init; } = GameSettings.Default.VSync;
    public float UiScale { get; init; } = GameSettings.Default.UiScale;
    public float InspectMouseSensitivity { get; init; } = 1f;

    public static readonly float[] UiScaleOptions = [0.5f, 0.75f, 1f, 1.25f, 1.5f];
    public static readonly float[] SensitivityOptions = [0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f];

    public static UserSettings Sanitize(UserSettings value) => value with
    {
        UiScale = Array.Exists(UiScaleOptions, option => option == value.UiScale)
            ? value.UiScale : Default.UiScale,
        InspectMouseSensitivity = Array.Exists(SensitivityOptions,
            option => option == value.InspectMouseSensitivity)
            ? value.InspectMouseSensitivity : Default.InspectMouseSensitivity
    };
}

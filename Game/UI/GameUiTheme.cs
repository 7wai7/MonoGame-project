using Microsoft.Xna.Framework;

namespace MonoGame_project.Game.UI;

/// <summary>Immutable color tokens. Views and style applicators use them; no Gum or GPU ownership.</summary>
public sealed record GameUiTheme
{
    public static GameUiTheme Dark { get; } = new()
    {
        TextPrimary = new(230, 234, 237),
        TextSecondary = new(177, 187, 194),
        Accent = new(165, 195, 194),
        DiagnosticsText = new(211, 211, 211),
        Overlay = new(7, 10, 13, 190),
        PanelFill = new(24, 29, 34),
        PanelBorder = new(69, 80, 88),
        ButtonFill = new(43, 52, 59),
        ButtonBorder = new(94, 108, 116),
        MenuPanelFill = new(27, 33, 38),
        MenuButtonFill = new(75, 89, 100),
        MenuButtonHover = new(234, 247, 255),
        MenuButtonPressed = new(177, 202, 218),
        MenuButtonFocused = new(213, 233, 242),
        MenuButtonDisabled = new(130, 136, 140)
    };

    private GameUiTheme() { }

    public Color TextPrimary { get; init; }
    public Color TextSecondary { get; init; }
    public Color Accent { get; init; }
    public Color DiagnosticsText { get; init; }
    public Color Overlay { get; init; }
    public Color PanelFill { get; init; }
    public Color PanelBorder { get; init; }
    public Color ButtonFill { get; init; }
    public Color ButtonBorder { get; init; }
    public Color MenuPanelFill { get; init; }
    public Color MenuButtonFill { get; init; }
    public Color MenuButtonHover { get; init; }
    public Color MenuButtonPressed { get; init; }
    public Color MenuButtonFocused { get; init; }
    public Color MenuButtonDisabled { get; init; }
}

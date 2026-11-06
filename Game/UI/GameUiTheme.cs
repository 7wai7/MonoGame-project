using Gum.Forms.Controls;
using Gum.Forms.DefaultVisuals.V3;
using Gum.GueDeriving;
using Gum.Managers;
using Gum.Wireframe;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoGame_project.Game.UI;

/// <summary>Shared palette and styles for the game's Gum controls.</summary>
public sealed class GameUiTheme : IDisposable
{
    public const int DefaultChamferSize = 0;
    public const int DefaultBorderThickness = 1;

    public static readonly Color TextPrimary = new(230, 234, 237);
    public static readonly Color TextSecondary = new(177, 187, 194);
    public static readonly Color Accent = new(165, 195, 194);

    private static readonly Color OverlayColor = new(7, 10, 13, 190);
    private static readonly Color PanelColor = new(24, 29, 34);
    private static readonly Color PanelBorderColor = new(69, 80, 88);
    private static readonly Color ButtonColor = new(43, 52, 59);
    private static readonly Color ButtonBorderColor = new(94, 108, 116);

    // UiScope.Own keeps this shared cache alive until all scene visuals detach.
    private readonly GameUiTextureCache _textures;
    private bool _disposed;

    public GameUiTheme(GraphicsDevice graphicsDevice) =>
        _textures = new GameUiTextureCache(
            graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice)));

    public RectangleRuntime CreateOverlay()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var overlay = new RectangleRuntime
        {
            FillColor = OverlayColor,
            StrokeWidth = 0f
        };
        overlay.Dock(Dock.Fill);
        return overlay;
    }

    /// <summary>Creates a styled frame; its owner controls size and placement.</summary>
    public ContainerRuntime CreatePanel(
        int chamferSize = DefaultChamferSize,
        int borderThickness = DefaultBorderThickness)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateFrameSize(chamferSize, borderThickness);
        var panel = new ContainerRuntime();
        var frame = new NineSliceRuntime
        {
            Texture = _textures.GetFrameTexture(chamferSize, borderThickness,
                PanelColor, PanelBorderColor),
            TextureAddress = TextureAddress.EntireTexture,
            Color = Color.White
        };
        frame.Dock(Dock.Fill);
        panel.Children.Add(frame);
        return panel;
    }

    /// <summary>Creates a styled control; its owner controls size and placement.</summary>
    public Button CreateButton(
        string text,
        int chamferSize = DefaultChamferSize,
        int borderThickness = DefaultBorderThickness)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(text);
        ValidateFrameSize(chamferSize, borderThickness);

        var button = new Button { Text = text };
        var visual = (ButtonVisual)button.Visual;
        visual.Background.Texture = _textures.GetFrameTexture(chamferSize,
            borderThickness, ButtonColor, ButtonBorderColor);
        visual.Background.TextureAddress = TextureAddress.EntireTexture;
        visual.Background.Dock(Dock.Fill);
        visual.TextInstance.Dock(Dock.Fill);
        visual.BackgroundColor = Color.White;
        visual.ForegroundColor = TextPrimary;
        visual.FocusedIndicatorColor = Accent;
        visual.TextInstance.FontSize = 16f;
        visual.TextInstance.UseFontSmoothing = true;
        return button;
    }

    /// <summary>A borderless menu background with subtle noise and fading edges.</summary>
    public ContainerRuntime CreatePausePanel()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var panel = new ContainerRuntime();
        var background = new SpriteRuntime
        {
            Texture = _textures.GetPausePanelTexture(),
            Color = Color.White
        };
        background.Dock(Dock.Fill);
        panel.Children.Add(background);
        return panel;
    }

    /// <summary>Menu-only button with explicit Gum states for mouse and focus.</summary>
    public Button CreatePauseButton(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(text);

        var button = new Button { Text = text };
        var visual = (ButtonVisual)button.Visual;
        visual.Background.Texture = _textures.GetPauseButtonTexture();
        visual.Background.TextureAddress = TextureAddress.EntireTexture;
        visual.Background.Dock(Dock.Fill);
        visual.TextInstance.Dock(Dock.Fill);
        visual.BackgroundColor = Color.White;
        visual.ForegroundColor = TextPrimary;
        visual.FocusedIndicator.Visible = false;
        visual.TextInstance.FontSize = 15f;
        visual.TextInstance.UseFontSmoothing = true;

        visual.States.Enabled.Clear();
        visual.States.Enabled.Apply = () => ApplyPauseButtonColors(visual,
            Color.White, TextPrimary);
        visual.States.Highlighted.Clear();
        visual.States.Highlighted.Apply = () => ApplyPauseButtonColors(visual,
            new Color(234, 247, 255), Color.White);
        visual.States.HighlightedFocused.Clear();
        visual.States.HighlightedFocused.Apply = visual.States.Highlighted.Apply;
        visual.States.Pushed.Clear();
        visual.States.Pushed.Apply = () => ApplyPauseButtonColors(visual,
            new Color(177, 202, 218), Color.White);
        visual.States.Focused.Clear();
        visual.States.Focused.Apply = () => ApplyPauseButtonColors(visual,
            new Color(213, 233, 242), Color.White);
        visual.States.Disabled.Clear();
        visual.States.Disabled.Apply = () => ApplyPauseButtonColors(visual,
            new Color(130, 136, 140), TextSecondary);
        visual.States.DisabledFocused.Clear();
        visual.States.DisabledFocused.Apply = visual.States.Disabled.Apply;
        button.UpdateState();
        return button;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _textures.Dispose();
        _disposed = true;
    }

    private static void ApplyPauseButtonColors(ButtonVisual visual,
        Color background, Color text)
    {
        visual.Background.Color = background;
        visual.TextInstance.Color = text;
    }

    private static void ValidateFrameSize(int chamferSize, int borderThickness)
    {
        if (chamferSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chamferSize));
        }

        if (borderThickness < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(borderThickness));
        }
    }
}

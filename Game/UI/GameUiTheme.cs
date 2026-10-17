using Gum.Forms.Controls;
using Gum.Forms.DefaultVisuals.V3;
using Gum.GueDeriving;
using Gum.Managers;
using Gum.Wireframe;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoGame_project.Game.UI;

/// <summary>Shared visuals and GPU textures for the game's dark UI style.</summary>
public sealed class GameUiTheme : IDisposable
{
    private const int FrameTextureSize = 48;
    private const int ChamferSize = 12;
    private const int BorderThickness = 2;

    private static readonly Color OverlayColor = new(3, 5, 8, 164);
    private static readonly Color PanelColor = new(18, 21, 27);
    private static readonly Color PanelBorderColor = new(62, 70, 82);
    private static readonly Color ButtonBorderColor = new(126, 140, 160);
    private static readonly Color SecondaryTextColor = new(190, 198, 210);

    private readonly Texture2D _panelTexture;
    private readonly Texture2D _buttonTexture;
    private bool _disposed;

    public GameUiTheme(GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        _panelTexture = CreateFrameTexture(
            graphicsDevice,
            chamferAllCorners: true,
            PanelColor,
            PanelBorderColor);
        _buttonTexture = CreateFrameTexture(
            graphicsDevice,
            chamferAllCorners: false,
            Color.Transparent,
            Color.White);
    }

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

    public ContainerRuntime CreatePanel(float width, float height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSize(width, height);

        var panel = new ContainerRuntime
        {
            Width = width,
            Height = height
        };
        var frame = new NineSliceRuntime
        {
            Texture = _panelTexture,
            TextureAddress = TextureAddress.EntireTexture,
            Color = Color.White
        };
        frame.Dock(Dock.Fill);
        panel.Children.Add(frame);
        return panel;
    }

    public Button CreateButton(
        string text,
        float width,
        float height,
        Action onClick)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(onClick);
        ValidateSize(width, height);

        var button = new Button
        {
            Text = text,
            Width = width,
            Height = height
        };

        var visual = (ButtonVisual)button.Visual;
        visual.Background.Texture = _buttonTexture;
        visual.Background.TextureAddress = TextureAddress.EntireTexture;
        visual.BackgroundColor = ButtonBorderColor;
        visual.ForegroundColor = SecondaryTextColor;
        visual.FocusedIndicator.Visible = false;
        visual.TextInstance.FontSize = 16f;
        visual.TextInstance.UseFontSmoothing = true;
        button.Click += (_, _) => onClick();
        return button;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _panelTexture.Dispose();
        _buttonTexture.Dispose();
        _disposed = true;
    }

    private static void ValidateSize(float width, float height)
    {
        if (!float.IsFinite(width) || width <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (!float.IsFinite(height) || height <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }
    }

    private static Texture2D CreateFrameTexture(
        GraphicsDevice graphicsDevice,
        bool chamferAllCorners,
        Color fillColor,
        Color borderColor)
    {
        var pixels = new Color[FrameTextureSize * FrameTextureSize];

        for (var y = 0; y < FrameTextureSize; y++)
        {
            for (var x = 0; x < FrameTextureSize; x++)
            {
                if (!IsInsideFrame(x, y, inset: 0, chamferAllCorners))
                {
                    continue;
                }

                var isInsideBorder = IsInsideFrame(
                    x,
                    y,
                    BorderThickness,
                    chamferAllCorners);
                pixels[y * FrameTextureSize + x] = isInsideBorder
                    ? fillColor
                    : borderColor;
            }
        }

        var texture = new Texture2D(
            graphicsDevice,
            FrameTextureSize,
            FrameTextureSize);
        texture.SetData(pixels);
        return texture;
    }

    private static bool IsInsideFrame(
        int x,
        int y,
        int inset,
        bool chamferAllCorners)
    {
        var farEdge = FrameTextureSize - 1 - inset;
        if (x < inset || x > farEdge || y < inset || y > farEdge)
        {
            return false;
        }

        var cut = ChamferSize - inset / 2;
        var fromLeft = x - inset;
        var fromTop = y - inset;
        var fromRight = farEdge - x;
        var fromBottom = farEdge - y;

        if (fromLeft + fromTop < cut || fromRight + fromBottom < cut)
        {
            return false;
        }

        return !chamferAllCorners ||
            (fromRight + fromTop >= cut && fromLeft + fromBottom >= cut);
    }
}

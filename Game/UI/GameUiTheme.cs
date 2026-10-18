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
    public const int DefaultChamferSize = 16;
    public const int DefaultBorderThickness = 2;

    private const int MinimumFrameTextureSize = 48;

    private static readonly Color OverlayColor = new(3, 5, 8, 164);
    private static readonly Color PanelColor = new(18, 21, 27);
    private static readonly Color PanelBorderColor = new(82, 174, 201);
    private static readonly Color ButtonBorderColor = new(115, 220, 244);
    private static readonly Color SecondaryTextColor = new(190, 198, 210);

    private static readonly FrameCorners PanelCorners = new(
        FrameCornerStyle.Chamfer,
        FrameCornerStyle.Chamfer,
        FrameCornerStyle.Chamfer,
        FrameCornerStyle.Chamfer);
    private static readonly FrameCorners ButtonCorners = new(
        FrameCornerStyle.OpenBorder,
        FrameCornerStyle.Square,
        FrameCornerStyle.OpenBorder,
        FrameCornerStyle.Square);

    private readonly GraphicsDevice _graphicsDevice;
    private readonly Dictionary<FrameTextureSpec, Texture2D> _frameTextures = new();
    private bool _disposed;

    public GameUiTheme(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
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

    /// <summary>Creates a panel; zero chamfer or border size disables that feature.</summary>
    public ContainerRuntime CreatePanel(
        float width,
        float height,
        int chamferSize = DefaultChamferSize,
        int borderThickness = DefaultBorderThickness)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSize(width, height);
        ValidateFrameSize(chamferSize, borderThickness);
        var texture = GetFrameTexture(new FrameTextureSpec(
            chamferSize,
            borderThickness,
            PanelCorners,
            PanelColor,
            PanelBorderColor));

        var panel = new ContainerRuntime
        {
            Width = width,
            Height = height
        };
        var frame = new NineSliceRuntime
        {
            Texture = texture,
            TextureAddress = TextureAddress.EntireTexture,
            Color = Color.White
        };
        frame.Dock(Dock.Fill);
        panel.Children.Add(frame);
        return panel;
    }

    /// <summary>Creates a button; zero chamfer or border size disables that feature.</summary>
    public Button CreateButton(
        string text,
        float width,
        float height,
        Action onClick,
        int chamferSize = DefaultChamferSize,
        int borderThickness = DefaultBorderThickness)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(onClick);
        ValidateSize(width, height);
        ValidateFrameSize(chamferSize, borderThickness);
        var texture = GetFrameTexture(new FrameTextureSpec(
            chamferSize,
            borderThickness,
            ButtonCorners,
            Color.Transparent,
            Color.White));

        var button = new Button
        {
            Text = text,
            Width = width,
            Height = height
        };

        var visual = (ButtonVisual)button.Visual;
        visual.Background.Texture = texture;
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

        foreach (var texture in _frameTextures.Values)
        {
            texture.Dispose();
        }

        _frameTextures.Clear();
        _disposed = true;
    }

    private Texture2D GetFrameTexture(FrameTextureSpec spec)
    {
        if (!_frameTextures.TryGetValue(spec, out var texture))
        {
            texture = CreateFrameTexture(_graphicsDevice, spec);
            _frameTextures.Add(spec, texture);
        }

        return texture;
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
        FrameTextureSpec spec)
    {
        // Nine-slice textures reserve one third of each axis for every corner.
        var cornerSize = checked(Math.Max(spec.ChamferSize, spec.BorderThickness) + 1);
        var textureSize = checked(3 * Math.Max(MinimumFrameTextureSize / 3, cornerSize));
        var dimensions = new FrameDimensions(textureSize, spec.ChamferSize, spec.BorderThickness);
        var pixels = new Color[checked(textureSize * textureSize)];

        for (var y = 0; y < textureSize; y++)
        {
            for (var x = 0; x < textureSize; x++)
            {
                if (!IsInsideFrame(x, y, inset: 0, dimensions, spec.Corners))
                {
                    continue;
                }

                var isInsideBorder = IsInsideFrame(
                    x,
                    y,
                    dimensions.BorderThickness,
                    dimensions,
                    spec.Corners);
                if (isInsideBorder)
                {
                    pixels[y * textureSize + x] = spec.FillColor;
                }
                else if (!IsOpenBorderCorner(x, y, dimensions, spec.Corners))
                {
                    pixels[y * textureSize + x] = spec.BorderColor;
                }
            }
        }

        var texture = new Texture2D(
            graphicsDevice,
            textureSize,
            textureSize);
        texture.SetData(pixels);
        return texture;
    }

    private static bool IsInsideFrame(
        int x,
        int y,
        int inset,
        FrameDimensions dimensions,
        FrameCorners corners)
    {
        var farEdge = dimensions.TextureSize - 1 - inset;
        if (x < inset || x > farEdge || y < inset || y > farEdge)
        {
            return false;
        }

        var cut = dimensions.ChamferSize - inset / 2;
        var fromLeft = x - inset;
        var fromTop = y - inset;
        var fromRight = farEdge - x;
        var fromBottom = farEdge - y;

        return (corners.TopLeft != FrameCornerStyle.Chamfer || fromLeft + fromTop >= cut) &&
            (corners.TopRight != FrameCornerStyle.Chamfer || fromRight + fromTop >= cut) &&
            (corners.BottomRight != FrameCornerStyle.Chamfer || fromRight + fromBottom >= cut) &&
            (corners.BottomLeft != FrameCornerStyle.Chamfer || fromLeft + fromBottom >= cut);
    }

    private static bool IsOpenBorderCorner(
        int x,
        int y,
        FrameDimensions dimensions,
        FrameCorners corners)
    {
        var nearLeft = x < dimensions.ChamferSize;
        var nearTop = y < dimensions.ChamferSize;
        var nearRight = x >= dimensions.TextureSize - dimensions.ChamferSize;
        var nearBottom = y >= dimensions.TextureSize - dimensions.ChamferSize;

        return (corners.TopLeft == FrameCornerStyle.OpenBorder && nearLeft && nearTop) ||
            (corners.TopRight == FrameCornerStyle.OpenBorder && nearRight && nearTop) ||
            (corners.BottomRight == FrameCornerStyle.OpenBorder && nearRight && nearBottom) ||
            (corners.BottomLeft == FrameCornerStyle.OpenBorder && nearLeft && nearBottom);
    }

    private enum FrameCornerStyle
    {
        Square,
        Chamfer,
        OpenBorder
    }

    private readonly record struct FrameCorners(
        FrameCornerStyle TopLeft,
        FrameCornerStyle TopRight,
        FrameCornerStyle BottomRight,
        FrameCornerStyle BottomLeft);

    private readonly record struct FrameDimensions(
        int TextureSize,
        int ChamferSize,
        int BorderThickness);

    private readonly record struct FrameTextureSpec(
        int ChamferSize,
        int BorderThickness,
        FrameCorners Corners,
        Color FillColor,
        Color BorderColor);
}

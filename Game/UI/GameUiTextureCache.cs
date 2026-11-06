using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoGame_project.Game.UI;

/// <summary>Scene-lifetime textures shared by Gum screens; never rebuilt per frame.</summary>
internal sealed class GameUiTextureCache : IDisposable
{
    private const int MinimumFrameTextureSize = 48;

    private readonly GraphicsDevice _graphicsDevice;
    private readonly Dictionary<FrameTextureSpec, Texture2D> _frames = new();
    private readonly Dictionary<UiNoiseTextureSpec, Texture2D> _noiseTextures = new();
    private bool _disposed;

    public GameUiTextureCache(GraphicsDevice graphicsDevice) =>
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));

    public Texture2D GetFrameTexture(int chamferSize, int borderThickness,
        Color fill, Color border)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(chamferSize);
        ArgumentOutOfRangeException.ThrowIfNegative(borderThickness);
        var spec = new FrameTextureSpec(chamferSize, borderThickness, fill, border);
        if (!_frames.TryGetValue(spec, out var texture))
        {
            texture = CreateFrameTexture(spec);
            _frames.Add(spec, texture);
        }

        return texture;
    }

    public Texture2D GetNoiseTexture(UiNoiseTextureSpec spec)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spec.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spec.Height);
        if (spec.Opacity is < 0 or > 255 ||
            !float.IsFinite(spec.FadeWidth) || spec.FadeWidth <= 0f ||
            !float.IsFinite(spec.NoiseStrength) || spec.NoiseStrength < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(spec));
        }

        if (!_noiseTextures.TryGetValue(spec, out var texture))
        {
            texture = CreateNoiseTexture(spec);
            _noiseTextures.Add(spec, texture);
        }

        return texture;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var texture in _frames.Values)
        {
            texture.Dispose();
        }

        _frames.Clear();
        foreach (var texture in _noiseTextures.Values)
        {
            texture.Dispose();
        }

        _noiseTextures.Clear();
        _disposed = true;
    }

    private Texture2D CreateFrameTexture(FrameTextureSpec spec)
    {
        // Nine-slice textures reserve one third of each axis for every corner.
        var cornerSize = checked(Math.Max(spec.ChamferSize, spec.BorderThickness) + 1);
        var textureSize = checked(3 * Math.Max(MinimumFrameTextureSize / 3, cornerSize));
        var pixels = new Color[checked(textureSize * textureSize)];

        for (var y = 0; y < textureSize; y++)
        {
            for (var x = 0; x < textureSize; x++)
            {
                if (!IsInsideFrame(x, y, 0, textureSize, spec.ChamferSize))
                {
                    continue;
                }

                pixels[y * textureSize + x] = IsInsideFrame(x, y,
                    spec.BorderThickness, textureSize, spec.ChamferSize)
                    ? spec.FillColor : spec.BorderColor;
            }
        }

        var texture = new Texture2D(_graphicsDevice, textureSize, textureSize);
        texture.SetData(pixels);
        return texture;
    }

    private static bool IsInsideFrame(int x, int y, int inset,
        int textureSize, int chamferSize)
    {
        var farEdge = textureSize - 1 - inset;
        if (x < inset || x > farEdge || y < inset || y > farEdge)
        {
            return false;
        }

        var cut = chamferSize - inset / 2;
        var fromLeft = x - inset;
        var fromTop = y - inset;
        var fromRight = farEdge - x;
        var fromBottom = farEdge - y;
        return fromLeft + fromTop >= cut &&
            fromRight + fromTop >= cut &&
            fromRight + fromBottom >= cut &&
            fromLeft + fromBottom >= cut;
    }

    private Texture2D CreateNoiseTexture(UiNoiseTextureSpec spec)
    {
        var pixels = new Color[checked(spec.Width * spec.Height)];
        for (var y = 0; y < spec.Height; y++)
        {
            for (var x = 0; x < spec.Width; x++)
            {
                var edgeDistance = MathF.Min(x, spec.Width - 1 - x);
                var fade = Math.Clamp(edgeDistance / spec.FadeWidth, 0f, 1f);
                fade = fade * fade * (3f - 2f * fade);
                var alpha = (int)MathF.Round(spec.Opacity * fade);
                var noise = (Perlin(x / 69f, y / 69f) +
                    0.35f * Perlin(x / 23f, y / 23f)) * spec.NoiseStrength;
                var red = Math.Clamp((int)MathF.Round(spec.BaseColor.R + noise), 0, 255);
                var green = Math.Clamp((int)MathF.Round(spec.BaseColor.G + noise), 0, 255);
                var blue = Math.Clamp((int)MathF.Round(spec.BaseColor.B + noise), 0, 255);
                pixels[y * spec.Width + x] = Color.FromNonPremultiplied(red, green, blue, alpha);
            }
        }

        var texture = new Texture2D(_graphicsDevice, spec.Width, spec.Height);
        texture.SetData(pixels);
        return texture;
    }

    private static float Perlin(float x, float y)
    {
        var cellX = (int)MathF.Floor(x);
        var cellY = (int)MathF.Floor(y);
        var localX = x - cellX;
        var localY = y - cellY;
        var blendX = localX * localX * (3f - 2f * localX);
        var blendY = localY * localY * (3f - 2f * localY);
        var top = MathHelper.Lerp(
            Gradient(cellX, cellY, localX, localY),
            Gradient(cellX + 1, cellY, localX - 1f, localY), blendX);
        var bottom = MathHelper.Lerp(
            Gradient(cellX, cellY + 1, localX, localY - 1f),
            Gradient(cellX + 1, cellY + 1, localX - 1f, localY - 1f), blendX);
        return MathHelper.Lerp(top, bottom, blendY);
    }

    private static float Gradient(int cellX, int cellY, float x, float y)
    {
        uint hash = unchecked((uint)cellX * 374761393u + (uint)cellY * 668265263u);
        hash = unchecked((hash ^ (hash >> 13)) * 1274126177u);
        return (hash & 7u) switch
        {
            0 => x,
            1 => -x,
            2 => y,
            3 => -y,
            4 => (x + y) * 0.7071068f,
            5 => (x - y) * 0.7071068f,
            6 => (-x + y) * 0.7071068f,
            _ => (-x - y) * 0.7071068f
        };
    }

    private readonly record struct FrameTextureSpec(int ChamferSize,
        int BorderThickness, Color FillColor, Color BorderColor);
}

internal readonly record struct UiNoiseTextureSpec(int Width, int Height,
    Color BaseColor, int Opacity, float FadeWidth, float NoiseStrength);

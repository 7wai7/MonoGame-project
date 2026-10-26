using System;

namespace MonoGame_project.Engine.Terrain;

/// <summary>Full-resolution CPU heightmap, sampled when creating terrain chunks.</summary>
public sealed class TerrainHeightMap
{
    private readonly float[] _values;

    public TerrainHeightMap(int width, int height, float[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (width < 2 || height < 2 || values.Length != checked(width * height))
        {
            throw new ArgumentException("Heightmap dimensions do not match its samples.", nameof(values));
        }

        var minimum = float.PositiveInfinity;
        var maximum = float.NegativeInfinity;
        foreach (var value in values)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentException("Heightmap values must be finite.", nameof(values));
            }

            minimum = MathF.Min(minimum, value);
            maximum = MathF.Max(maximum, value);
        }

        if (maximum <= minimum)
        {
            throw new ArgumentException("Heightmap must contain more than one height.", nameof(values));
        }

        Width = width;
        Height = height;
        Minimum = minimum;
        Maximum = maximum;
        _values = values;
    }

    public int Width { get; }
    public int Height { get; }
    public float Minimum { get; }
    public float Maximum { get; }

    public float Sample(float u, float v)
    {
        var x = Math.Clamp(u, 0f, 1f) * (Width - 1);
        var y = Math.Clamp(v, 0f, 1f) * (Height - 1);
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var x1 = Math.Min(x0 + 1, Width - 1);
        var y1 = Math.Min(y0 + 1, Height - 1);
        var tx = x - x0;
        var ty = y - y0;
        var top = Lerp(_values[y0 * Width + x0], _values[y0 * Width + x1], tx);
        var bottom = Lerp(_values[y1 * Width + x0], _values[y1 * Width + x1], tx);
        return Lerp(top, bottom, ty);
    }

    private static float Lerp(float first, float second, float amount) =>
        first + (second - first) * amount;
}

using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame_project.Engine.Assets;
using MonoGame_project.Engine.Diagnostics;
using MonoGame_project.Engine.Terrain;

namespace MonoGame_project.Game.Terrain;

/// <summary>Converts the project's float EXR terrain assets into CPU heights and a GPU diffuse texture.</summary>
internal static class ExrTerrainAssets
{
    private static readonly byte[] SrgbLookup = CreateSrgbLookup();

    public static TerrainHeightMap LoadHeights(string path)
    {
        using var reader = new OpenExrScanlineReader(path);
        var channel = reader.GetChannelIndex("Y");
        if (channel < 0)
        {
            channel = reader.GetChannelIndex("R");
        }

        if (channel < 0)
        {
            throw new InvalidDataException($"'{path}' has neither a Y nor an R height channel.");
        }

        var values = new float[checked(reader.Width * reader.Height)];
        for (var row = 0; row < reader.Height; row++)
        {
            var scanline = reader.ReadScanline(row);
            scanline.Slice(channel * reader.Width, reader.Width)
                .CopyTo(values.AsSpan(row * reader.Width, reader.Width));
        }

        return new TerrainHeightMap(reader.Width, reader.Height, values);
    }

    public static Texture2D LoadDiffuse(
        GraphicsDevice graphicsDevice,
        string path,
        int expectedWidth,
        int expectedHeight)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        using var reader = new OpenExrScanlineReader(path);
        if (reader.Width != expectedWidth || reader.Height != expectedHeight)
        {
            throw new InvalidDataException(
                $"'{path}' must match the heightmap dimensions {expectedWidth}x{expectedHeight}.");
        }

        var redIndex = reader.GetChannelIndex("R");
        var greenIndex = reader.GetChannelIndex("G");
        var blueIndex = reader.GetChannelIndex("B");
        if (redIndex < 0 || greenIndex < 0 || blueIndex < 0)
        {
            throw new InvalidDataException($"'{path}' must contain R, G and B channels.");
        }

        Color[] pixels;
        using (StartupProfiler.Measure("Diffuse EXR read + sRGB conversion"))
        {
            pixels = new Color[checked(reader.Width * reader.Height)];
            for (var row = 0; row < reader.Height; row++)
            {
                var scanline = reader.ReadScanline(row);
                var red = scanline.Slice(redIndex * reader.Width, reader.Width);
                var green = scanline.Slice(greenIndex * reader.Width, reader.Width);
                var blue = scanline.Slice(blueIndex * reader.Width, reader.Width);
                var destination = pixels.AsSpan(row * reader.Width, reader.Width);
                for (var x = 0; x < reader.Width; x++)
                {
                    destination[x] = new Color(
                        ToSrgb(red[x]),
                        ToSrgb(green[x]),
                        ToSrgb(blue[x]));
                }
            }
        }

        // MonoGame textures are 8-bit for this BasicEffect material. Height data
        // stays as EXR floats on the CPU, so color conversion cannot quantize physics.
        using (StartupProfiler.Measure("Diffuse mipmap generation + GPU upload"))
        {
            var texture = new Texture2D(
                graphicsDevice, reader.Width, reader.Height, mipmap: true, SurfaceFormat.Color);
            try
            {
                var level = 0;
                var width = reader.Width;
                var height = reader.Height;
                while (true)
                {
                    texture.SetData(level, null, pixels, 0, pixels.Length);
                    if (width == 1 && height == 1)
                    {
                        break;
                    }

                    var nextWidth = Math.Max(1, width / 2);
                    var nextHeight = Math.Max(1, height / 2);
                    var next = new Color[nextWidth * nextHeight];
                    for (var y = 0; y < nextHeight; y++)
                    {
                        for (var x = 0; x < nextWidth; x++)
                        {
                            var x0 = x * 2;
                            var x1 = Math.Min(x0 + 1, width - 1);
                            var y0 = y * 2;
                            var y1 = Math.Min(y0 + 1, height - 1);
                            var a = pixels[y0 * width + x0];
                            var b = pixels[y0 * width + x1];
                            var c = pixels[y1 * width + x0];
                            var d = pixels[y1 * width + x1];
                            next[y * nextWidth + x] = new Color(
                                (a.R + b.R + c.R + d.R + 2) / 4,
                                (a.G + b.G + c.G + d.G + 2) / 4,
                                (a.B + b.B + c.B + d.B + 2) / 4);
                        }
                    }

                    pixels = next;
                    width = nextWidth;
                    height = nextHeight;
                    level++;
                }

                return texture;
            }
            catch
            {
                texture.Dispose();
                throw;
            }
        }
    }

    private static byte ToSrgb(float linear)
    {
        if (float.IsNaN(linear) || linear <= 0f)
        {
            return 0;
        }

        if (linear >= 1f)
        {
            return 255;
        }

        return SrgbLookup[(int)MathF.Round(linear * (SrgbLookup.Length - 1))];
    }

    private static byte[] CreateSrgbLookup()
    {
        var lookup = new byte[65_536];
        for (var i = 0; i < lookup.Length; i++)
        {
            var linear = i / (float)(lookup.Length - 1);
            var srgb = linear <= 0.0031308f
                ? linear * 12.92f
                : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
            lookup[i] = (byte)MathF.Round(srgb * 255f);
        }

        return lookup;
    }
}

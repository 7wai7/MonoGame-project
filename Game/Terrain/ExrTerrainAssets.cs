using System;
using System.IO;
using MonoGame_project.Engine.Assets;
using MonoGame_project.Engine.Terrain;

namespace MonoGame_project.Game.Terrain;

/// <summary>Reads the project's EXR heightmap into CPU terrain data.</summary>
internal static class ExrTerrainAssets
{
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
}

using System;
using Microsoft.Xna.Framework;
using MonoGame_project.Engine.Terrain;

namespace MonoGame_project.Game.Terrain;

/// <summary>Maps the full EXR image to a finite world rectangle shared by all chunks.</summary>
internal sealed class HeightMapTerrainSource
{
    private readonly TerrainHeightMap _map;
    private readonly float _heightScale;
    private readonly float _heightOffset;

    public HeightMapTerrainSource(
        TerrainHeightMap map,
        Vector2 worldOrigin,
        Vector2 worldSize,
        float heightScale,
        float heightOffset)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!float.IsFinite(worldOrigin.X) || !float.IsFinite(worldOrigin.Y) ||
            !float.IsFinite(worldSize.X) || !float.IsFinite(worldSize.Y) ||
            worldSize.X <= 0f || worldSize.Y <= 0f ||
            !float.IsFinite(heightScale) || heightScale <= 0f ||
            !float.IsFinite(heightOffset))
        {
            throw new ArgumentException("Terrain image-to-world mapping is invalid.");
        }

        _map = map;
        WorldOrigin = worldOrigin;
        WorldSize = worldSize;
        _heightScale = heightScale;
        _heightOffset = heightOffset;
    }

    public Vector2 WorldOrigin { get; }
    public Vector2 WorldSize { get; }

    public float SampleHeight(float worldX, float worldZ)
    {
        var u = (worldX - WorldOrigin.X) / WorldSize.X;
        var v = (worldZ - WorldOrigin.Y) / WorldSize.Y;
        var raw = _map.Sample(u, v);
        // Preserve the EXR's raw range. Gaea's Terrain Definition determines
        // how those values correspond to meters; per-image normalization would
        // silently change the vertical scale of every imported landscape.
        return raw * _heightScale + _heightOffset;
    }
}

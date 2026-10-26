using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame_project.Engine.Rendering.ThreeD;

namespace MonoGame_project.Engine.Terrain;

/// <summary>Builds CPU render geometry from immutable terrain heights.</summary>
public static class TerrainMeshFactory
{
    public static MeshData Create(TerrainHeightField heights, float maxHeightError = 0f)
    {
        ArgumentNullException.ThrowIfNull(heights);
        return Create(heights, maxHeightError,
            Vector2.Zero,
            new Vector2(10f, 10f));
    }

    /// <summary>
    /// Builds geometry whose UVs cover a shared world rectangle. Adjacent
    /// chunks therefore sample the same pixels along their common edge.
    /// </summary>
    public static MeshData Create(
        TerrainHeightField heights,
        float maxHeightError,
        Vector2 textureWorldOrigin,
        Vector2 textureWorldSize,
        int maxPatchCellSpan = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(heights);
        if (!float.IsFinite(textureWorldOrigin.X) || !float.IsFinite(textureWorldOrigin.Y) ||
            !float.IsFinite(textureWorldSize.X) || !float.IsFinite(textureWorldSize.Y) ||
            textureWorldSize.X <= 0f || textureWorldSize.Y <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(textureWorldSize));
        }

        var surface = TerrainSurfaceBuilder.CreateAdaptive(
            heights, maxHeightError, maxPatchCellSpan: maxPatchCellSpan);
        return CreateMeshData(heights, surface.Positions.Span, surface.IndexArray,
            textureWorldOrigin, textureWorldSize);
    }

    /// <summary>Exact source-grid geometry, useful for nearby terrain without adaptive-subdivision cost.</summary>
    public static MeshData CreateFullResolution(
        TerrainHeightField heights,
        Vector2 textureWorldOrigin,
        Vector2 textureWorldSize)
    {
        ArgumentNullException.ThrowIfNull(heights);
        var positions = TerrainSurfaceBuilder.CreatePositions(heights);
        var indices = TerrainSurfaceBuilder.CreateIndices(heights.CellCount);
        return CreateMeshData(heights, positions, indices, textureWorldOrigin, textureWorldSize);
    }

    private static MeshData CreateMeshData(
        TerrainHeightField heights,
        ReadOnlySpan<Vector3> positions,
        int[] indices,
        Vector2 textureWorldOrigin,
        Vector2 textureWorldSize)
    {
        if (!float.IsFinite(textureWorldOrigin.X) || !float.IsFinite(textureWorldOrigin.Y) ||
            !float.IsFinite(textureWorldSize.X) || !float.IsFinite(textureWorldSize.Y) ||
            textureWorldSize.X <= 0f || textureWorldSize.Y <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(textureWorldSize));
        }

        var vertices = new VertexPositionNormalTexture[positions.Length];
        for (var index = 0; index < positions.Length; index++)
        {
            var position = positions[index];
            var x = position.X / heights.CellSize;
            var z = position.Z / heights.CellSize;
            var slopeX = (heights.SampleInterpolatedHeight(x + 1f, z) -
                heights.SampleInterpolatedHeight(x - 1f, z)) / (2f * heights.CellSize);
            var slopeZ = (heights.SampleInterpolatedHeight(x, z + 1f) -
                heights.SampleInterpolatedHeight(x, z - 1f)) / (2f * heights.CellSize);
            var normal = Vector3.Normalize(new Vector3(-slopeX, 1f, -slopeZ));
            var textureCoordinate = new Vector2(
                (heights.OriginX + position.X - textureWorldOrigin.X) / textureWorldSize.X,
                (heights.OriginZ + position.Z - textureWorldOrigin.Y) / textureWorldSize.Y);
            vertices[index] = new VertexPositionNormalTexture(position, normal, textureCoordinate);
        }

        return new MeshData(vertices, indices);
    }
}

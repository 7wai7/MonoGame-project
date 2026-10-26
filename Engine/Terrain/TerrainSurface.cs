using System;
using Microsoft.Xna.Framework;

namespace MonoGame_project.Engine.Terrain;

/// <summary>
/// Immutable triangulation of a height field, in chunk-local coordinates.
/// Rendering and collision may use separate surfaces with different errors.
/// </summary>
public sealed class TerrainSurface
{
    private readonly Vector3[] _positions;
    private readonly int[] _indices;
    private int[]? _wireIndices;

    internal TerrainSurface(Vector3[] positions, int[] indices)
    {
        _positions = positions;
        _indices = indices;
    }

    public ReadOnlyMemory<Vector3> Positions => _positions;
    public ReadOnlyMemory<int> Indices => _indices;
    /// <summary>Built only when collider wireframes are first requested.</summary>
    public ReadOnlyMemory<int> WireIndices =>
        _wireIndices ??= TerrainSurfaceBuilder.CreateWireIndices(_indices);
    internal int[] IndexArray => _indices;
    public int VertexCount => _positions.Length;
    public int TriangleCount => _indices.Length / 3;
}

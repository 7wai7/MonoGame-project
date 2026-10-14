using System;
using Microsoft.Xna.Framework.Graphics;

namespace MonoGame_project.Engine.Rendering.ThreeD;

/// <summary>
/// CPU-side triangle geometry that can be created without a GraphicsDevice.
/// A Mesh uploads this data to GPU buffers when it is constructed.
/// </summary>
public sealed class MeshData
{
    private readonly VertexPositionNormalTexture[] _vertices;
    private readonly int[] _indices;

    public MeshData(VertexPositionNormalTexture[] vertices, int[] indices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);

        if (vertices.Length == 0)
        {
            throw new ArgumentException("Mesh data must contain at least one vertex.", nameof(vertices));
        }

        if (indices.Length == 0 || indices.Length % 3 != 0)
        {
            throw new ArgumentException(
                "Triangle-list mesh data must contain a non-zero multiple of three indices.",
                nameof(indices));
        }

        for (var index = 0; index < indices.Length; index++)
        {
            if ((uint)indices[index] >= (uint)vertices.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(indices),
                    $"Index {indices[index]} at position {index} does not reference an existing vertex.");
            }
        }

        _vertices = vertices;
        _indices = indices;
    }

    public int VertexCount => _vertices.Length;
    public int IndexCount => _indices.Length;
    public int PrimitiveCount => _indices.Length / 3;

    public ReadOnlyMemory<VertexPositionNormalTexture> Vertices => _vertices;
    public ReadOnlyMemory<int> Indices => _indices;

    internal VertexPositionNormalTexture[] VertexArray => _vertices;
    internal int[] IndexArray => _indices;
}

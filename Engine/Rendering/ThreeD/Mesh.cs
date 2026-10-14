using System;
using Microsoft.Xna.Framework.Graphics;

namespace MonoGame_project.Engine.Rendering.ThreeD;

/// <summary>GPU vertex and index buffers for one reusable 3D mesh.</summary>
public sealed class Mesh : IDisposable
{
    private readonly VertexBuffer _vertexBuffer;
    private readonly IndexBuffer _indexBuffer;
    private bool _disposed;

    public Mesh(
        GraphicsDevice graphicsDevice,
        VertexPositionNormalTexture[] vertices,
        int[] indices)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);

        if (vertices.Length == 0)
        {
            throw new ArgumentException("A mesh must contain at least one vertex.", nameof(vertices));
        }

        if (indices.Length == 0 || indices.Length % 3 != 0)
        {
            throw new ArgumentException(
                "A triangle-list mesh must contain a non-zero multiple of three indices.",
                nameof(indices));
        }

        VertexCount = vertices.Length;
        PrimitiveCount = indices.Length / 3;

        _vertexBuffer = new VertexBuffer(
            graphicsDevice,
            typeof(VertexPositionNormalTexture),
            vertices.Length,
            BufferUsage.WriteOnly);
        _vertexBuffer.SetData(vertices);

        _indexBuffer = new IndexBuffer(
            graphicsDevice,
            IndexElementSize.ThirtyTwoBits,
            indices.Length,
            BufferUsage.WriteOnly);
        _indexBuffer.SetData(indices);
    }

    public int VertexCount { get; }
    public int PrimitiveCount { get; }
    public bool IsDisposed => _disposed;

    internal void Draw(GraphicsDevice graphicsDevice)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        graphicsDevice.SetVertexBuffer(_vertexBuffer);
        graphicsDevice.Indices = _indexBuffer;
        graphicsDevice.DrawIndexedPrimitives(
            PrimitiveType.TriangleList,
            baseVertex: 0,
            startIndex: 0,
            primitiveCount: PrimitiveCount);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _indexBuffer.Dispose();
        _vertexBuffer.Dispose();
        _disposed = true;
    }
}

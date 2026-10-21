using System;
using Microsoft.Xna.Framework.Graphics;

namespace MonoGame_project.Engine.Rendering.ThreeD;

/// <summary>GPU vertex and index buffers for one reusable 3D mesh.</summary>
public sealed class Mesh : IDisposable
{
    private readonly VertexBuffer _vertexBuffer;
    private readonly IndexBuffer _indexBuffer;
    private bool _disposed;

    public Mesh(GraphicsDevice graphicsDevice, MeshData data)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentNullException.ThrowIfNull(data);

        VertexCount = data.VertexCount;
        PrimitiveCount = data.PrimitiveCount;
        SourceData = data;

        _vertexBuffer = new VertexBuffer(
            graphicsDevice,
            typeof(VertexPositionNormalTexture),
            data.VertexCount,
            BufferUsage.WriteOnly);
        _vertexBuffer.SetData(data.VertexArray);

        _indexBuffer = new IndexBuffer(
            graphicsDevice,
            IndexElementSize.ThirtyTwoBits,
            data.IndexCount,
            BufferUsage.WriteOnly);
        _indexBuffer.SetData(data.IndexArray);
    }

    public int VertexCount { get; }
    public int PrimitiveCount { get; }
    /// <summary>CPU geometry retained for exact mesh picking and diagnostics.</summary>
    public MeshData SourceData { get; }
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

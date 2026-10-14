using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoGame_project.Engine.Rendering.ThreeD;

/// <summary>Creates CPU-side geometry without Content Pipeline or GPU access.</summary>
public static class ProceduralMeshFactory
{
    public static MeshData CreateBox(Vector3 size)
    {
        if (size.X <= 0f || size.Y <= 0f || size.Z <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Every box dimension must be positive.");
        }

        var half = size * 0.5f;
        var vertices = new VertexPositionNormalTexture[24];
        var indices = new int[36];
        var vertexOffset = 0;
        var indexOffset = 0;

        AddFace(
            vertices,
            indices,
            ref vertexOffset,
            ref indexOffset,
            new Vector3(-half.X, half.Y, half.Z),
            new Vector3(half.X, half.Y, half.Z),
            new Vector3(half.X, -half.Y, half.Z),
            new Vector3(-half.X, -half.Y, half.Z),
            Vector3.Forward * -1f);

        AddFace(
            vertices,
            indices,
            ref vertexOffset,
            ref indexOffset,
            new Vector3(half.X, half.Y, -half.Z),
            new Vector3(-half.X, half.Y, -half.Z),
            new Vector3(-half.X, -half.Y, -half.Z),
            new Vector3(half.X, -half.Y, -half.Z),
            Vector3.Forward);

        AddFace(
            vertices,
            indices,
            ref vertexOffset,
            ref indexOffset,
            new Vector3(half.X, half.Y, half.Z),
            new Vector3(half.X, half.Y, -half.Z),
            new Vector3(half.X, -half.Y, -half.Z),
            new Vector3(half.X, -half.Y, half.Z),
            Vector3.Right);

        AddFace(
            vertices,
            indices,
            ref vertexOffset,
            ref indexOffset,
            new Vector3(-half.X, half.Y, -half.Z),
            new Vector3(-half.X, half.Y, half.Z),
            new Vector3(-half.X, -half.Y, half.Z),
            new Vector3(-half.X, -half.Y, -half.Z),
            Vector3.Left);

        AddFace(
            vertices,
            indices,
            ref vertexOffset,
            ref indexOffset,
            new Vector3(-half.X, half.Y, -half.Z),
            new Vector3(half.X, half.Y, -half.Z),
            new Vector3(half.X, half.Y, half.Z),
            new Vector3(-half.X, half.Y, half.Z),
            Vector3.Up);

        AddFace(
            vertices,
            indices,
            ref vertexOffset,
            ref indexOffset,
            new Vector3(-half.X, -half.Y, half.Z),
            new Vector3(half.X, -half.Y, half.Z),
            new Vector3(half.X, -half.Y, -half.Z),
            new Vector3(-half.X, -half.Y, -half.Z),
            Vector3.Down);

        return new MeshData(vertices, indices);
    }

    private static void AddFace(
        VertexPositionNormalTexture[] vertices,
        int[] indices,
        ref int vertexOffset,
        ref int indexOffset,
        Vector3 topLeft,
        Vector3 topRight,
        Vector3 bottomRight,
        Vector3 bottomLeft,
        Vector3 normal)
    {
        vertices[vertexOffset] = new VertexPositionNormalTexture(topLeft, normal, new Vector2(0f, 0f));
        vertices[vertexOffset + 1] = new VertexPositionNormalTexture(topRight, normal, new Vector2(1f, 0f));
        vertices[vertexOffset + 2] = new VertexPositionNormalTexture(bottomRight, normal, new Vector2(1f, 1f));
        vertices[vertexOffset + 3] = new VertexPositionNormalTexture(bottomLeft, normal, new Vector2(0f, 1f));

        indices[indexOffset] = vertexOffset;
        indices[indexOffset + 1] = vertexOffset + 1;
        indices[indexOffset + 2] = vertexOffset + 2;
        indices[indexOffset + 3] = vertexOffset;
        indices[indexOffset + 4] = vertexOffset + 2;
        indices[indexOffset + 5] = vertexOffset + 3;

        vertexOffset += 4;
        indexOffset += 6;
    }
}

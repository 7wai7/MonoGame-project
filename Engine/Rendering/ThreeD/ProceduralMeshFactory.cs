using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoGame_project.Engine.Rendering.ThreeD;

/// <summary>Creates CPU-side geometry without Content Pipeline or GPU access.</summary>
public static class ProceduralMeshFactory
{
    // -------------------------------------------------------------------------
    // Public primitive factories
    // -------------------------------------------------------------------------

    public static MeshData CreateBox(Vector3 size)
    {
        if (!float.IsFinite(size.X) ||
            !float.IsFinite(size.Y) ||
            !float.IsFinite(size.Z) ||
            size.X <= 0f ||
            size.Y <= 0f ||
            size.Z <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(size),
                "Every box dimension must be finite and positive.");
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

    /// <summary>Creates a UV sphere centered on the local origin.</summary>
    public static MeshData CreateSphere(
        float radius,
        int longitudeSegments = 24,
        int latitudeSegments = 16)
    {
        ValidatePositive(radius, nameof(radius), "Sphere radius");
        ValidateSegments(longitudeSegments, 3, nameof(longitudeSegments));
        ValidateSegments(latitudeSegments, 2, nameof(latitudeSegments));

        var ringCount = checked(latitudeSegments + 1);
        var ringRadii = new float[ringCount];
        var ringHeights = new float[ringCount];
        var normalRadii = new float[ringCount];
        var normalHeights = new float[ringCount];

        for (var ring = 0; ring < ringCount; ring++)
        {
            var latitude = MathHelper.PiOver2 - MathHelper.Pi * ring / latitudeSegments;
            var normalRadius = MathF.Cos(latitude);
            var normalHeight = MathF.Sin(latitude);

            normalRadii[ring] = ring == 0 || ring == ringCount - 1
                ? 0f
                : normalRadius;
            normalHeights[ring] = normalHeight;
            ringRadii[ring] = normalRadii[ring] * radius;
            ringHeights[ring] = normalHeight * radius;
        }

        return CreateSurfaceOfRevolution(
            ringRadii,
            ringHeights,
            normalRadii,
            normalHeights,
            longitudeSegments);
    }

    /// <summary>Creates a Y-axis cylinder centered on the local origin.</summary>
    public static MeshData CreateCylinder(
        float radius,
        float height,
        int segments = 24)
    {
        ValidatePositive(radius, nameof(radius), "Cylinder radius");
        ValidatePositive(height, nameof(height), "Cylinder height");
        ValidateSegments(segments, 3, nameof(segments));

        var sideVertexCount = checked((segments + 1) * 2);
        var capVertexCount = checked(segments + 2);
        var vertices = new VertexPositionNormalTexture[
            checked(sideVertexCount + capVertexCount * 2)];
        var indices = new int[checked(segments * 12)];
        var halfHeight = height * 0.5f;

        for (var segment = 0; segment <= segments; segment++)
        {
            var u = (float)segment / segments;
            var angle = MathF.Tau * u;
            var normal = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
            var radial = normal * radius;
            var vertex = segment * 2;

            vertices[vertex] = new VertexPositionNormalTexture(
                new Vector3(radial.X, -halfHeight, radial.Z),
                normal,
                new Vector2(u, 1f));
            vertices[vertex + 1] = new VertexPositionNormalTexture(
                new Vector3(radial.X, halfHeight, radial.Z),
                normal,
                new Vector2(u, 0f));
        }

        var indexOffset = 0;
        for (var segment = 0; segment < segments; segment++)
        {
            var bottom = segment * 2;
            var top = bottom + 1;
            var nextBottom = bottom + 2;
            var nextTop = bottom + 3;

            AddTriangle(indices, ref indexOffset, bottom, top, nextTop);
            AddTriangle(indices, ref indexOffset, bottom, nextTop, nextBottom);
        }

        var topCapOffset = sideVertexCount;
        AddCapVertices(
            vertices,
            topCapOffset,
            radius,
            halfHeight,
            Vector3.Up,
            segments);

        var bottomCapOffset = topCapOffset + capVertexCount;
        AddCapVertices(
            vertices,
            bottomCapOffset,
            radius,
            -halfHeight,
            Vector3.Down,
            segments);

        for (var segment = 0; segment < segments; segment++)
        {
            var current = segment + 1;
            var next = current + 1;
            AddTriangle(
                indices,
                ref indexOffset,
                topCapOffset,
                topCapOffset + next,
                topCapOffset + current);
            AddTriangle(
                indices,
                ref indexOffset,
                bottomCapOffset,
                bottomCapOffset + current,
                bottomCapOffset + next);
        }

        return new MeshData(vertices, indices);
    }

    /// <summary>
    /// Creates a Y-axis capsule. Length is its cylindrical section and does not
    /// include either hemispherical end.
    /// </summary>
    public static MeshData CreateCapsule(
        float radius,
        float length,
        int radialSegments = 24,
        int hemisphereSegments = 8)
    {
        ValidatePositive(radius, nameof(radius), "Capsule radius");
        ValidateNonNegative(length, nameof(length), "Capsule length");
        ValidateSegments(radialSegments, 3, nameof(radialSegments));
        ValidateSegments(hemisphereSegments, 1, nameof(hemisphereSegments));

        if (!float.IsFinite(length + 2f * radius))
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                "Capsule total height must be finite.");
        }

        if (length == 0f)
        {
            return CreateSphere(
                radius,
                radialSegments,
                checked(hemisphereSegments * 2));
        }

        var ringCount = checked(hemisphereSegments * 2 + 2);
        var ringRadii = new float[ringCount];
        var ringHeights = new float[ringCount];
        var normalRadii = new float[ringCount];
        var normalHeights = new float[ringCount];
        var halfLength = length * 0.5f;

        ringHeights[0] = halfLength + radius;
        normalHeights[0] = 1f;

        for (var ring = 1; ring <= hemisphereSegments; ring++)
        {
            var angle = MathHelper.PiOver2 -
                MathHelper.PiOver2 * ring / hemisphereSegments;
            var normalRadius = MathF.Cos(angle);
            var normalHeight = MathF.Sin(angle);

            ringRadii[ring] = normalRadius * radius;
            ringHeights[ring] = halfLength + normalHeight * radius;
            normalRadii[ring] = normalRadius;
            normalHeights[ring] = normalHeight;
        }

        var lowerSeam = hemisphereSegments + 1;
        ringRadii[lowerSeam] = radius;
        ringHeights[lowerSeam] = -halfLength;
        normalRadii[lowerSeam] = 1f;

        for (var ring = 1; ring <= hemisphereSegments; ring++)
        {
            var profileIndex = lowerSeam + ring;
            var angle = -MathHelper.PiOver2 * ring / hemisphereSegments;
            var normalRadius = MathF.Cos(angle);
            var normalHeight = MathF.Sin(angle);

            ringRadii[profileIndex] = ring == hemisphereSegments
                ? 0f
                : normalRadius * radius;
            ringHeights[profileIndex] = -halfLength + normalHeight * radius;
            normalRadii[profileIndex] = ring == hemisphereSegments
                ? 0f
                : normalRadius;
            normalHeights[profileIndex] = normalHeight;
        }

        return CreateSurfaceOfRevolution(
            ringRadii,
            ringHeights,
            normalRadii,
            normalHeights,
            radialSegments);
    }

    /// <summary>
    /// Creates a Y-axis cone whose local origin matches Jitter's center of mass.
    /// </summary>
    public static MeshData CreateCone(
        float radius,
        float height,
        int segments = 24)
    {
        ValidatePositive(radius, nameof(radius), "Cone radius");
        ValidatePositive(height, nameof(height), "Cone height");
        ValidateSegments(segments, 3, nameof(segments));

        var sideVertexCount = checked(segments * 3);
        var baseVertexCount = checked(segments + 2);
        var vertices = new VertexPositionNormalTexture[
            checked(sideVertexCount + baseVertexCount)];
        var indices = new int[checked(segments * 6)];
        var baseY = -height * 0.25f;
        var apex = new Vector3(0f, height * 0.75f, 0f);
        var vertexOffset = 0;
        var indexOffset = 0;

        for (var segment = 0; segment < segments; segment++)
        {
            var firstU = (float)segment / segments;
            var secondU = (float)(segment + 1) / segments;
            var firstAngle = MathF.Tau * firstU;
            var secondAngle = MathF.Tau * secondU;
            var middleAngle = (firstAngle + secondAngle) * 0.5f;

            var firstNormal = CreateConeNormal(firstAngle, radius, height);
            var middleNormal = CreateConeNormal(middleAngle, radius, height);
            var secondNormal = CreateConeNormal(secondAngle, radius, height);
            var first = new Vector3(
                radius * MathF.Cos(firstAngle),
                baseY,
                radius * MathF.Sin(firstAngle));
            var second = new Vector3(
                radius * MathF.Cos(secondAngle),
                baseY,
                radius * MathF.Sin(secondAngle));

            vertices[vertexOffset] = new VertexPositionNormalTexture(
                first,
                firstNormal,
                new Vector2(firstU, 1f));
            vertices[vertexOffset + 1] = new VertexPositionNormalTexture(
                apex,
                middleNormal,
                new Vector2((firstU + secondU) * 0.5f, 0f));
            vertices[vertexOffset + 2] = new VertexPositionNormalTexture(
                second,
                secondNormal,
                new Vector2(secondU, 1f));
            AddTriangle(
                indices,
                ref indexOffset,
                vertexOffset,
                vertexOffset + 1,
                vertexOffset + 2);
            vertexOffset += 3;
        }

        var baseOffset = sideVertexCount;
        AddCapVertices(vertices, baseOffset, radius, baseY, Vector3.Down, segments);
        for (var segment = 0; segment < segments; segment++)
        {
            AddTriangle(
                indices,
                ref indexOffset,
                baseOffset,
                baseOffset + segment + 1,
                baseOffset + segment + 2);
        }

        return new MeshData(vertices, indices);
    }

    // -------------------------------------------------------------------------
    // Shared curved-surface construction
    // -------------------------------------------------------------------------

    private static MeshData CreateSurfaceOfRevolution(
        float[] ringRadii,
        float[] ringHeights,
        float[] normalRadii,
        float[] normalHeights,
        int radialSegments)
    {
        var ringCount = ringRadii.Length;
        var rowSize = checked(radialSegments + 1);
        var vertices = new VertexPositionNormalTexture[checked(ringCount * rowSize)];
        var triangleCount = 0;

        for (var ring = 0; ring < ringCount - 1; ring++)
        {
            if (ringRadii[ring] > 0f)
            {
                triangleCount = checked(triangleCount + radialSegments);
            }

            if (ringRadii[ring + 1] > 0f)
            {
                triangleCount = checked(triangleCount + radialSegments);
            }
        }

        var indices = new int[checked(triangleCount * 3)];
        var top = ringHeights[0];
        var totalHeight = top - ringHeights[ringCount - 1];

        for (var ring = 0; ring < ringCount; ring++)
        {
            var v = (top - ringHeights[ring]) / totalHeight;
            for (var segment = 0; segment <= radialSegments; segment++)
            {
                var u = (float)segment / radialSegments;
                var angle = MathF.Tau * u;
                var cosine = MathF.Cos(angle);
                var sine = MathF.Sin(angle);
                var position = new Vector3(
                    ringRadii[ring] * cosine,
                    ringHeights[ring],
                    ringRadii[ring] * sine);
                var normal = new Vector3(
                    normalRadii[ring] * cosine,
                    normalHeights[ring],
                    normalRadii[ring] * sine);

                vertices[ring * rowSize + segment] = new VertexPositionNormalTexture(
                    position,
                    Vector3.Normalize(normal),
                    new Vector2(u, v));
            }
        }

        var indexOffset = 0;
        for (var ring = 0; ring < ringCount - 1; ring++)
        {
            for (var segment = 0; segment < radialSegments; segment++)
            {
                var current = ring * rowSize + segment;
                var next = current + 1;
                var below = current + rowSize;
                var belowNext = below + 1;

                if (ringRadii[ring] > 0f)
                {
                    AddTriangle(indices, ref indexOffset, current, next, belowNext);
                }

                if (ringRadii[ring + 1] > 0f)
                {
                    AddTriangle(indices, ref indexOffset, current, belowNext, below);
                }
            }
        }

        return new MeshData(vertices, indices);
    }

    private static void AddCapVertices(
        VertexPositionNormalTexture[] vertices,
        int offset,
        float radius,
        float y,
        Vector3 normal,
        int segments)
    {
        vertices[offset] = new VertexPositionNormalTexture(
            new Vector3(0f, y, 0f),
            normal,
            new Vector2(0.5f));

        for (var segment = 0; segment <= segments; segment++)
        {
            var angle = MathF.Tau * segment / segments;
            var cosine = MathF.Cos(angle);
            var sine = MathF.Sin(angle);
            vertices[offset + segment + 1] = new VertexPositionNormalTexture(
                new Vector3(radius * cosine, y, radius * sine),
                normal,
                new Vector2(cosine * 0.5f + 0.5f, sine * 0.5f + 0.5f));
        }
    }

    private static Vector3 CreateConeNormal(float angle, float radius, float height) =>
        Vector3.Normalize(new Vector3(
            height * MathF.Cos(angle),
            radius,
            height * MathF.Sin(angle)));

    private static void AddTriangle(
        int[] indices,
        ref int indexOffset,
        int first,
        int second,
        int third)
    {
        indices[indexOffset++] = first;
        indices[indexOffset++] = second;
        indices[indexOffset++] = third;
    }

    // -------------------------------------------------------------------------
    // Input validation
    // -------------------------------------------------------------------------

    private static void ValidatePositive(float value, string parameterName, string label)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"{label} must be finite and positive.");
        }
    }

    private static void ValidateNonNegative(float value, string parameterName, string label)
    {
        if (!float.IsFinite(value) || value < 0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"{label} must be finite and non-negative.");
        }
    }

    private static void ValidateSegments(int value, int minimum, string parameterName)
    {
        if (value < minimum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Segment count must be at least {minimum}.");
        }
    }

    // -------------------------------------------------------------------------
    // Box construction
    // -------------------------------------------------------------------------

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

using Microsoft.Xna.Framework;
using MonoGame_project.Ecs;

namespace MonoGame_project.Engine.Rendering.ThreeD;

/// <summary>Finds the closest visible mesh triangle intersected by a world-space ray.</summary>
public static class MeshRaycaster
{
    public static bool TryPick(
        World world,
        Ray ray,
        float maxDistance,
        out Entity entity,
        out float distance)
    {
        entity = Entity.None;
        distance = maxDistance;
        if (!float.IsFinite(maxDistance) || maxDistance <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDistance));
        }

        var worldDirection = ray.Direction;
        if (worldDirection.LengthSquared() < 1e-12f)
        {
            return false;
        }

        worldDirection.Normalize();

        foreach (var candidate in world.Query<Transform3D, MeshRenderer>())
        {
            var renderer = world.Get<MeshRenderer>(candidate);
            if (!renderer.IsVisible || renderer.Mesh is null || renderer.Mesh.IsDisposed)
            {
                continue;
            }

            var transform = world.Get<Transform3D>(candidate);
            if (MathF.Abs(transform.Scale.X * transform.Scale.Y * transform.Scale.Z) < 1e-8f)
            {
                continue;
            }

            // Inverse-transforming the ray preserves its world-distance parameter.
            var inverse = Matrix.Invert(transform.WorldMatrix);
            var origin = Vector3.Transform(ray.Position, inverse);
            var direction = Vector3.TransformNormal(worldDirection, inverse);
            var data = renderer.Mesh.SourceData;
            if (!IntersectsBounds(origin, direction, data.Bounds, distance))
            {
                continue;
            }

            var vertices = data.Vertices.Span;
            var indices = data.Indices.Span;

            for (var i = 0; i < indices.Length; i += 3)
            {
                if (TryIntersectTriangle(
                    origin,
                    direction,
                    vertices[indices[i]].Position,
                    vertices[indices[i + 1]].Position,
                    vertices[indices[i + 2]].Position,
                    out var hitDistance) &&
                    hitDistance < distance && hitDistance >= 0f)
                {
                    entity = candidate;
                    distance = hitDistance;
                }
            }
        }

        return !entity.IsNone;
    }

    private static bool IntersectsBounds(
        in Vector3 origin,
        in Vector3 direction,
        in BoundingBox bounds,
        float maximumDistance)
    {
        var near = 0f;
        var far = maximumDistance;
        for (var axis = 0; axis < 3; axis++)
        {
            var position = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
            var velocity = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
            var minimum = axis == 0 ? bounds.Min.X : axis == 1 ? bounds.Min.Y : bounds.Min.Z;
            var maximum = axis == 0 ? bounds.Max.X : axis == 1 ? bounds.Max.Y : bounds.Max.Z;

            if (MathF.Abs(velocity) < 1e-8f)
            {
                if (position < minimum || position > maximum)
                {
                    return false;
                }

                continue;
            }

            var enter = (minimum - position) / velocity;
            var exit = (maximum - position) / velocity;
            if (enter > exit)
            {
                (enter, exit) = (exit, enter);
            }

            near = MathF.Max(near, enter);
            far = MathF.Min(far, exit);
            if (near > far)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryIntersectTriangle(
        in Vector3 origin,
        in Vector3 direction,
        in Vector3 a,
        in Vector3 b,
        in Vector3 c,
        out float distance)
    {
        // Two-sided Möller–Trumbore test: picking must work from either side.
        var edge1 = b - a;
        var edge2 = c - a;
        var perpendicular = Vector3.Cross(direction, edge2);
        var determinant = Vector3.Dot(edge1, perpendicular);
        if (MathF.Abs(determinant) < 1e-7f)
        {
            distance = 0f;
            return false;
        }

        var inverse = 1f / determinant;
        var offset = origin - a;
        var u = Vector3.Dot(offset, perpendicular) * inverse;
        if (u < 0f || u > 1f)
        {
            distance = 0f;
            return false;
        }

        var cross = Vector3.Cross(offset, edge1);
        var v = Vector3.Dot(direction, cross) * inverse;
        if (v < 0f || u + v > 1f)
        {
            distance = 0f;
            return false;
        }

        distance = Vector3.Dot(edge2, cross) * inverse;
        return distance >= 0f;
    }
}

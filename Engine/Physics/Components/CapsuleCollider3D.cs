using System;
using MonoGame_project.Ecs;

namespace MonoGame_project.Engine.Physics.Components;

/// <summary>
/// A Y-axis capsule. Length is the cylindrical section and excludes both
/// hemispherical ends, so its total height is Length + 2 * Radius.
/// </summary>
public struct CapsuleCollider3D : IComponent
{
    public CapsuleCollider3D(float radius, float length)
    {
        Radius = radius;
        Length = length;
        Validate();
    }

    public float Radius;
    public float Length;

    public readonly float TotalHeight => Length + 2f * Radius;

    public readonly void Validate()
    {
        if (!float.IsFinite(Radius) || Radius <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Radius),
                "Capsule collider radius must be finite and positive.");
        }

        if (!float.IsFinite(Length) || Length < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Length),
                "Capsule collider length must be finite and non-negative.");
        }
    }
}

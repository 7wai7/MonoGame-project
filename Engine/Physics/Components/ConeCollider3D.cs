using System;
using MonoGame_project.Ecs;

namespace MonoGame_project.Engine.Physics.Components;

/// <summary>A cone collision shape whose local symmetry axis is Y.</summary>
public struct ConeCollider3D : IComponent
{
    public ConeCollider3D(float radius, float height)
    {
        Radius = radius;
        Height = height;
        Validate();
    }

    public float Radius;
    public float Height;

    public readonly void Validate()
    {
        if (!float.IsFinite(Radius) || Radius <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Radius),
                "Cone collider radius must be finite and positive.");
        }

        if (!float.IsFinite(Height) || Height <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Height),
                "Cone collider height must be finite and positive.");
        }
    }
}

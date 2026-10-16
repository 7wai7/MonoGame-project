using System;
using MonoGame_project.Ecs;

namespace MonoGame_project.Engine.Physics.Components;

/// <summary>A sphere collision shape centered on the entity's physics pose.</summary>
public struct SphereCollider3D : IComponent
{
    public SphereCollider3D(float radius)
    {
        Radius = radius;
        Validate();
    }

    public float Radius;

    public readonly void Validate()
    {
        if (!float.IsFinite(Radius) || Radius <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Radius),
                "Sphere collider radius must be finite and positive.");
        }
    }
}

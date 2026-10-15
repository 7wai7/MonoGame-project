using System;
using Microsoft.Xna.Framework;
using MonoGame_project.Ecs;

namespace MonoGame_project.Engine.Physics.Components;

/// <summary>A box collision shape centered on the entity's physics pose.</summary>
public struct BoxCollider3D : IComponent
{
    public BoxCollider3D(Vector3 size)
    {
        Size = size;
        Validate();
    }

    public Vector3 Size;

    public readonly void Validate()
    {
        if (!float.IsFinite(Size.X) ||
            !float.IsFinite(Size.Y) ||
            !float.IsFinite(Size.Z) ||
            Size.X <= 0f ||
            Size.Y <= 0f ||
            Size.Z <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Size),
                "Every box collider dimension must be finite and positive.");
        }
    }
}

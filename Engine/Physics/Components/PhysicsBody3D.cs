using System;
using MonoGame_project.Ecs;

namespace MonoGame_project.Engine.Physics.Components;

/// <summary>
/// Engine-facing rigid-body settings. Jolt owns the
/// native body; it is not stored in this ECS component.
/// </summary>
public struct PhysicsBody3D : IComponent
{
    public PhysicsBody3D(
        PhysicsMotionType motionType,
        float mass = 1f,
        float friction = 0.2f,
        float restitution = 0f,
        bool affectedByGravity = true)
    {
        MotionType = motionType;
        Mass = mass;
        Friction = friction;
        Restitution = restitution;
        AffectedByGravity = affectedByGravity;
        Validate();
    }

    public PhysicsMotionType MotionType;
    public float Mass;
    public float Friction;
    public float Restitution;
    public bool AffectedByGravity;

    public readonly void Validate()
    {
        if (!Enum.IsDefined(MotionType))
        {
            throw new ArgumentOutOfRangeException(nameof(MotionType));
        }

        if (!float.IsFinite(Mass) || Mass <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(Mass), "Body mass must be finite and positive.");
        }

        if (!float.IsFinite(Friction) || Friction < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(Friction), "Friction must be finite and non-negative.");
        }

        if (!float.IsFinite(Restitution) || Restitution < 0f || Restitution > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Restitution),
                "Restitution must be between zero and one.");
        }
    }
}

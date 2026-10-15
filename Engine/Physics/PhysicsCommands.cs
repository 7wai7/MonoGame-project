using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MonoGame_project.Ecs;

namespace MonoGame_project.Engine.Physics;

/// <summary>
/// Queues one-shot physics changes so they are applied safely before the next
/// fixed simulation step.
/// </summary>
public sealed class PhysicsCommands
{
    private readonly Queue<PhysicsCommand> _commands = [];

    public int Count => _commands.Count;

    public void ApplyForce(Entity entity, Vector3 force) =>
        Enqueue(PhysicsCommandKind.ApplyForce, entity, force);

    public void ApplyTorque(Entity entity, Vector3 torque) =>
        Enqueue(PhysicsCommandKind.ApplyTorque, entity, torque);

    public void ApplyImpulse(Entity entity, Vector3 impulse) =>
        Enqueue(PhysicsCommandKind.ApplyImpulse, entity, impulse);

    public void ApplyImpulse(Entity entity, Vector3 impulse, Vector3 worldPosition)
    {
        EnsureEntity(entity);
        _commands.Enqueue(new PhysicsCommand(
            PhysicsCommandKind.ApplyImpulseAtPosition,
            entity,
            impulse,
            worldPosition,
            Quaternion.Identity,
            default));
    }

    public void SetVelocity(Entity entity, Vector3 velocity) =>
        Enqueue(PhysicsCommandKind.SetVelocity, entity, velocity);

    public void Teleport(Entity entity, Vector3 position, Quaternion rotation)
    {
        EnsureEntity(entity);
        _commands.Enqueue(new PhysicsCommand(
            PhysicsCommandKind.Teleport,
            entity,
            position,
            Vector3.Zero,
            rotation,
            default));
    }

    /// <summary>
    /// Moves a kinematic body to the target pose during the next fixed step.
    /// Queue this command again whenever the target changes.
    /// </summary>
    public void SetKinematicTarget(Entity entity, Vector3 position, Quaternion rotation)
    {
        EnsureEntity(entity);
        _commands.Enqueue(new PhysicsCommand(
            PhysicsCommandKind.SetKinematicTarget,
            entity,
            position,
            Vector3.Zero,
            rotation,
            default));
    }

    public void SetMotionType(Entity entity, PhysicsMotionType motionType)
    {
        EnsureEntity(entity);
        if (!Enum.IsDefined(motionType))
        {
            throw new ArgumentOutOfRangeException(nameof(motionType));
        }

        _commands.Enqueue(new PhysicsCommand(
            PhysicsCommandKind.SetMotionType,
            entity,
            Vector3.Zero,
            Vector3.Zero,
            Quaternion.Identity,
            motionType));
    }

    public void Clear() => _commands.Clear();

    internal void Drain(Action<PhysicsCommand> apply)
    {
        while (_commands.TryDequeue(out var command))
        {
            apply(command);
        }
    }

    private void Enqueue(PhysicsCommandKind kind, Entity entity, Vector3 value)
    {
        EnsureEntity(entity);
        _commands.Enqueue(new PhysicsCommand(
            kind,
            entity,
            value,
            Vector3.Zero,
            Quaternion.Identity,
            default));
    }

    private static void EnsureEntity(Entity entity)
    {
        if (entity.IsNone)
        {
            throw new ArgumentException("A physics command requires a valid entity.", nameof(entity));
        }
    }
}

internal enum PhysicsCommandKind
{
    ApplyForce,
    ApplyTorque,
    ApplyImpulse,
    ApplyImpulseAtPosition,
    SetVelocity,
    Teleport,
    SetKinematicTarget,
    SetMotionType
}

internal readonly record struct PhysicsCommand(
    PhysicsCommandKind Kind,
    Entity Entity,
    Vector3 Value,
    Vector3 SecondaryValue,
    Quaternion Rotation,
    PhysicsMotionType MotionType);

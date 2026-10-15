using System;
using System.Collections.Generic;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Physics.Components;
using MonoGame_project.Engine.Rendering.ThreeD;
using MonoGame_project.Engine.Systems;
using MonoGame_project.Engine.Time;

namespace MonoGame_project.Engine.Physics;

/// <summary>Creates, steps, synchronizes, and removes scene physics bodies.</summary>
public sealed class PhysicsSystem : GameSystem, IFixedUpdateSystem
{
    private readonly PhysicsSettings _settings;
    private readonly List<Entity> _bodyCandidates = [];
    private readonly HashSet<Entity> _pendingPoseRemoval = [];
    private PhysicsSimulation _simulation = null!;
    private PhysicsCommands _commands = null!;
    private PhysicsCollisionEvents _collisionEvents = null!;

    public PhysicsSystem()
        : this(PhysicsSettings.Default)
    {
    }

    public PhysicsSystem(PhysicsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    protected override void OnInitialize()
    {
        _collisionEvents = new PhysicsCollisionEvents();
        _simulation = new PhysicsSimulation(_settings, _collisionEvents);
        _commands = new PhysicsCommands();

        World.SetResource(_simulation);
        World.SetResource(_commands);
        World.SetResource(_collisionEvents);
        World.ComponentRemoving += OnComponentRemoving;

        EnsurePhysicsBodies();
    }

    public void FixedUpdate(World world, in FixedTime time)
    {
        var deltaSeconds = time.DeltaSeconds;

        CleanupInactivePoses();
        EnsurePhysicsBodies();
        CapturePreviousPoses();

        StopKinematicBodies();
        _commands.Drain(command => ApplyCommand(command, deltaSeconds));
        _simulation.Step(deltaSeconds);
        _collisionEvents.Publish();

        SynchronizeTransforms();
    }

    protected override void OnShutdown()
    {
        World.ComponentRemoving -= OnComponentRemoving;
        _commands.Clear();
        _collisionEvents.Clear();
        _pendingPoseRemoval.Clear();
        _bodyCandidates.Clear();

        World.RemoveResource<PhysicsCollisionEvents>();
        World.RemoveResource<PhysicsCommands>();
        World.RemoveResource<PhysicsSimulation>();

        _simulation.Dispose();
        _simulation = null!;
        _commands = null!;
        _collisionEvents = null!;
    }

    private void EnsurePhysicsBodies()
    {
        _bodyCandidates.Clear();
        foreach (var entity in World.Query<PhysicsBody3D>())
        {
            if (!World.Has<Transform3D>(entity))
            {
                throw new InvalidOperationException(
                    $"{entity} has PhysicsBody3D but does not have Transform3D.");
            }

            if (!World.Has<BoxCollider3D>(entity))
            {
                throw new InvalidOperationException(
                    $"{entity} has PhysicsBody3D but does not have a supported collider.");
            }

            _bodyCandidates.Add(entity);
        }

        foreach (var entity in _bodyCandidates)
        {
            var transform = World.Get<Transform3D>(entity);
            var body = World.Get<PhysicsBody3D>(entity);
            var collider = World.Get<BoxCollider3D>(entity);
            var change = _simulation.EnsureBoxBody(entity, transform, body, collider);
            if (change == PhysicsBodyChange.None)
            {
                continue;
            }

            if (World.Has<PhysicsPose3D>(entity))
            {
                ref var pose = ref World.Get<PhysicsPose3D>(entity);
                pose = new PhysicsPose3D(transform.Position, transform.Rotation);
            }
            else
            {
                World.Add(entity, new PhysicsPose3D(transform.Position, transform.Rotation));
            }
        }
    }

    private void CapturePreviousPoses()
    {
        foreach (var entity in World.Query<PhysicsBody3D, PhysicsPose3D>())
        {
            ref var pose = ref World.Get<PhysicsPose3D>(entity);
            if (!pose.IsActive)
            {
                continue;
            }

            pose.PreviousPosition = pose.CurrentPosition;
            pose.PreviousRotation = pose.CurrentRotation;
        }
    }

    private void StopKinematicBodies()
    {
        foreach (var entity in World.Query<PhysicsBody3D>())
        {
            var body = World.Get<PhysicsBody3D>(entity);
            if (body.MotionType == PhysicsMotionType.Kinematic)
            {
                _simulation.StopKinematic(entity);
            }
        }
    }

    private void ApplyCommand(PhysicsCommand command, float deltaSeconds)
    {
        if (!World.IsAlive(command.Entity) || !World.Has<PhysicsBody3D>(command.Entity))
        {
            return;
        }

        switch (command.Kind)
        {
            case PhysicsCommandKind.ApplyForce:
                _simulation.ApplyForce(command.Entity, command.Value);
                break;

            case PhysicsCommandKind.ApplyTorque:
                _simulation.ApplyTorque(command.Entity, command.Value);
                break;

            case PhysicsCommandKind.ApplyImpulse:
                _simulation.ApplyImpulse(command.Entity, command.Value);
                break;

            case PhysicsCommandKind.ApplyImpulseAtPosition:
                _simulation.ApplyImpulse(
                    command.Entity,
                    command.Value,
                    command.SecondaryValue);
                break;

            case PhysicsCommandKind.SetVelocity:
                _simulation.SetVelocity(command.Entity, command.Value);
                break;

            case PhysicsCommandKind.Teleport:
                ApplyTeleport(command);
                break;

            case PhysicsCommandKind.SetKinematicTarget:
                _simulation.SetKinematicTarget(
                    command.Entity,
                    command.Value,
                    command.Rotation,
                    deltaSeconds);
                break;

            case PhysicsCommandKind.SetMotionType:
                ApplyMotionType(command.Entity, command.MotionType);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
    }

    private void ApplyTeleport(in PhysicsCommand command)
    {
        if (!_simulation.Teleport(command.Entity, command.Value, command.Rotation))
        {
            return;
        }

        ref var transform = ref World.Get<Transform3D>(command.Entity);
        transform.Position = command.Value;
        transform.Rotation = command.Rotation;

        if (World.Has<PhysicsPose3D>(command.Entity))
        {
            ref var pose = ref World.Get<PhysicsPose3D>(command.Entity);
            pose = new PhysicsPose3D(command.Value, command.Rotation);
        }
    }

    private void ApplyMotionType(Entity entity, PhysicsMotionType motionType)
    {
        if (!_simulation.SetMotionType(entity, motionType))
        {
            return;
        }

        ref var body = ref World.Get<PhysicsBody3D>(entity);
        body.MotionType = motionType;
    }

    private void SynchronizeTransforms()
    {
        foreach (var entity in World.Query<Transform3D, PhysicsBody3D, PhysicsPose3D>())
        {
            if (!_simulation.TryGetPose(entity, out var position, out var rotation))
            {
                continue;
            }

            ref var transform = ref World.Get<Transform3D>(entity);
            ref var pose = ref World.Get<PhysicsPose3D>(entity);

            transform.Position = position;
            transform.Rotation = rotation;
            pose.CurrentPosition = position;
            pose.CurrentRotation = rotation;
            pose.IsActive = true;
        }
    }

    private void OnComponentRemoving(World world, Entity entity, Type componentType)
    {
        if (componentType != typeof(PhysicsBody3D) &&
            componentType != typeof(BoxCollider3D) &&
            componentType != typeof(Transform3D))
        {
            return;
        }

        _simulation.RemoveBody(entity);
        if (world.Has<PhysicsPose3D>(entity))
        {
            ref var pose = ref world.Get<PhysicsPose3D>(entity);
            pose.IsActive = false;
            _pendingPoseRemoval.Add(entity);
        }
    }

    private void CleanupInactivePoses()
    {
        foreach (var entity in _pendingPoseRemoval)
        {
            if (World.IsAlive(entity) && World.Has<PhysicsPose3D>(entity))
            {
                World.Remove<PhysicsPose3D>(entity);
            }
        }

        _pendingPoseRemoval.Clear();
    }
}

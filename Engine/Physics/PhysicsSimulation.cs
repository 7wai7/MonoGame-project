using System;
using System.Collections.Generic;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Microsoft.Xna.Framework;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Physics.Components;
using MonoGame_project.Engine.Rendering.ThreeD;
using JitterWorld = Jitter2.World;

namespace MonoGame_project.Engine.Physics;

/// <summary>
/// Owns the Jitter world and keeps native physics objects outside ECS components.
/// </summary>
public sealed class PhysicsSimulation : IDisposable
{
    // -------------------------------------------------------------------------
    // Simulation state and entity-to-Jitter mappings
    // -------------------------------------------------------------------------

    private readonly JitterWorld _nativeWorld;
    private readonly Dictionary<Entity, BodyEntry> _bodies = [];
    private readonly Dictionary<RigidBody, Entity> _entitiesByBody = [];
    private readonly HashSet<CollisionPair> _activeCollisions = [];
    private readonly List<CollisionPair> _collisionsToRemove = [];
    private readonly PhysicsCollisionEvents _collisionEvents;
    private bool _disposed;

    // -------------------------------------------------------------------------
    // Construction and public diagnostics
    // -------------------------------------------------------------------------

    public PhysicsSimulation(PhysicsSettings settings, PhysicsCollisionEvents collisionEvents)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(collisionEvents);
        settings.Validate();

        Settings = settings;
        _collisionEvents = collisionEvents;
        _nativeWorld = new JitterWorld
        {
            Gravity = PhysicsConvert.ToJitter(settings.Gravity)
        };
    }

    public PhysicsSettings Settings { get; }
    public int BodyCount => _bodies.Count;

    // -------------------------------------------------------------------------
    // ECS body synchronization and lifetime
    //
    // EnsureBoxBody is the main synchronization entry point. It creates a
    // missing body, rebuilds it when its shape changes, or updates its settings.
    // -------------------------------------------------------------------------

    internal PhysicsBodyChange EnsureBoxBody(
        Entity entity,
        in Transform3D transform,
        in PhysicsBody3D settings,
        in BoxCollider3D collider)
    {
        ThrowIfDisposed();
        settings.Validate();
        collider.Validate();
        ValidatePose(transform.Position, transform.Rotation);

        if (!_bodies.TryGetValue(entity, out var entry))
        {
            CreateBoxBody(entity, transform.Position, transform.Rotation, settings, collider);
            return PhysicsBodyChange.Created;
        }

        if (entry.ColliderSize != collider.Size)
        {
            RebuildBoxBody(entity, entry, settings, collider);
            return PhysicsBodyChange.Rebuilt;
        }

        ApplySettings(entry.Body, settings, entry.Settings);
        entry.Settings = settings;
        return PhysicsBodyChange.None;
    }

    public bool Contains(Entity entity)
    {
        ThrowIfDisposed();
        return _bodies.ContainsKey(entity);
    }

    public bool RemoveBody(Entity entity)
    {
        ThrowIfDisposed();
        if (!_bodies.Remove(entity, out var entry))
        {
            return false;
        }

        RemoveActiveCollisions(entity);
        entry.Body.BeginCollide -= OnBeginCollide;
        entry.Body.EndCollide -= OnEndCollide;
        _entitiesByBody.Remove(entry.Body);

        if (entry.Body.IsValid)
        {
            _nativeWorld.Remove(entry.Body);
        }

        return true;
    }

    internal bool TryGetPose(Entity entity, out Vector3 position, out Quaternion rotation)
    {
        ThrowIfDisposed();
        if (_bodies.TryGetValue(entity, out var entry))
        {
            position = PhysicsConvert.ToMonoGame(entry.Body.Position);
            rotation = PhysicsConvert.ToMonoGame(entry.Body.Orientation);
            return true;
        }

        position = Vector3.Zero;
        rotation = Quaternion.Identity;
        return false;
    }

    // -------------------------------------------------------------------------
    // Operations requested through PhysicsCommands
    // -------------------------------------------------------------------------

    internal bool ApplyForce(Entity entity, in Vector3 force)
    {
        if (!TryGetBody(entity, out var body))
        {
            return false;
        }

        var value = PhysicsConvert.ToJitter(force);
        body.AddForce(value);
        return true;
    }

    internal bool ApplyTorque(Entity entity, in Vector3 torque)
    {
        if (!TryGetBody(entity, out var body))
        {
            return false;
        }

        body.Torque += PhysicsConvert.ToJitter(torque);
        return true;
    }

    internal bool ApplyImpulse(Entity entity, in Vector3 impulse)
    {
        if (!TryGetBody(entity, out var body))
        {
            return false;
        }

        var value = PhysicsConvert.ToJitter(impulse);
        body.ApplyImpulse(value);
        return true;
    }

    internal bool ApplyImpulse(Entity entity, in Vector3 impulse, in Vector3 worldPosition)
    {
        if (!TryGetBody(entity, out var body))
        {
            return false;
        }

        var impulseValue = PhysicsConvert.ToJitter(impulse);
        var positionValue = PhysicsConvert.ToJitter(worldPosition);
        body.ApplyImpulse(impulseValue, positionValue);
        return true;
    }

    internal bool SetVelocity(Entity entity, in Vector3 velocity)
    {
        if (!TryGetBody(entity, out var body))
        {
            return false;
        }

        body.Velocity = PhysicsConvert.ToJitter(velocity);
        return true;
    }

    internal bool SetMotionType(Entity entity, PhysicsMotionType motionType)
    {
        if (!TryGetEntry(entity, out var entry))
        {
            return false;
        }

        entry.Body.MotionType = ToJitterMotionType(motionType);
        entry.Settings.MotionType = motionType;
        return true;
    }

    internal bool SetKinematicTarget(
        Entity entity,
        in Vector3 targetPosition,
        in Quaternion targetRotation,
        float deltaSeconds)
    {
        if (!TryGetBody(entity, out var body))
        {
            return false;
        }

        if (body.MotionType != MotionType.Kinematic)
        {
            throw new InvalidOperationException(
                $"{entity} has a kinematic target but its physics body is not kinematic.");
        }

        ValidatePose(targetPosition, targetRotation);
        if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        var currentPosition = PhysicsConvert.ToMonoGame(body.Position);
        var currentRotation = Quaternion.Normalize(PhysicsConvert.ToMonoGame(body.Orientation));
        var normalizedTarget = Quaternion.Normalize(targetRotation);

        body.Velocity = PhysicsConvert.ToJitter(
            (targetPosition - currentPosition) / deltaSeconds);
        body.AngularVelocity = PhysicsConvert.ToJitter(
            CalculateAngularVelocity(currentRotation, normalizedTarget, deltaSeconds));
        return true;
    }

    internal bool StopKinematic(Entity entity)
    {
        if (!TryGetBody(entity, out var body))
        {
            return false;
        }

        if (body.MotionType == MotionType.Kinematic)
        {
            body.Velocity = default;
            body.AngularVelocity = default;
        }

        return true;
    }

    internal bool Teleport(
        Entity entity,
        in Vector3 position,
        in Quaternion rotation)
    {
        if (!TryGetBody(entity, out var body))
        {
            return false;
        }

        ValidatePose(position, rotation);
        body.Position = PhysicsConvert.ToJitter(position);
        body.Orientation = PhysicsConvert.ToJitter(Quaternion.Normalize(rotation));
        body.ClearContactCache();

        if (body.MotionType != MotionType.Static)
        {
            body.Velocity = default;
            body.AngularVelocity = default;
        }

        return true;
    }

    // -------------------------------------------------------------------------
    // Fixed simulation step and disposal
    // -------------------------------------------------------------------------

    internal void Step(float deltaSeconds)
    {
        ThrowIfDisposed();
        _nativeWorld.Step(deltaSeconds, Settings.UseMultithreading);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _bodies.Clear();
        _entitiesByBody.Clear();
        _activeCollisions.Clear();
        _collisionsToRemove.Clear();
        _nativeWorld.Dispose();
        _disposed = true;
    }

    // -------------------------------------------------------------------------
    // Native Jitter body creation
    // -------------------------------------------------------------------------

    private void CreateBoxBody(
        Entity entity,
        in Vector3 position,
        in Quaternion rotation,
        in PhysicsBody3D settings,
        in BoxCollider3D collider)
    {
        var body = _nativeWorld.CreateRigidBody();
        try
        {
            body.Position = PhysicsConvert.ToJitter(position);
            body.Orientation = PhysicsConvert.ToJitter(Quaternion.Normalize(rotation));
            body.AddShape(new BoxShape(collider.Size.X, collider.Size.Y, collider.Size.Z));
            ApplySettings(body, settings, previousSettings: default, forceMassUpdate: true);

            _bodies.Add(entity, new BodyEntry(body, settings, collider.Size));
            _entitiesByBody.Add(body, entity);
            body.BeginCollide += OnBeginCollide;
            body.EndCollide += OnEndCollide;
        }
        catch
        {
            body.BeginCollide -= OnBeginCollide;
            body.EndCollide -= OnEndCollide;
            _bodies.Remove(entity);
            _entitiesByBody.Remove(body);
            if (body.IsValid)
            {
                _nativeWorld.Remove(body);
            }

            throw;
        }
    }

    // -------------------------------------------------------------------------
    // Collision callbacks and active collision-pair tracking
    // -------------------------------------------------------------------------

    private void OnBeginCollide(Arbiter arbiter)
    {
        if (!TryCreateCollisionPair(arbiter, out var pair) || !_activeCollisions.Add(pair))
        {
            return;
        }

        _collisionEvents.AddStarted(pair.ToCollision());
    }

    private void OnEndCollide(Arbiter arbiter)
    {
        if (!TryCreateCollisionPair(arbiter, out var pair) || !_activeCollisions.Remove(pair))
        {
            return;
        }

        _collisionEvents.AddEnded(pair.ToCollision());
    }

    private bool TryCreateCollisionPair(Arbiter arbiter, out CollisionPair pair)
    {
        if (_entitiesByBody.TryGetValue(arbiter.Body1, out var first) &&
            _entitiesByBody.TryGetValue(arbiter.Body2, out var second))
        {
            pair = CollisionPair.Create(first, second);
            return true;
        }

        pair = default;
        return false;
    }

    private void RemoveActiveCollisions(Entity entity)
    {
        _collisionsToRemove.Clear();
        foreach (var pair in _activeCollisions)
        {
            if (pair.First == entity || pair.Second == entity)
            {
                _collisionsToRemove.Add(pair);
            }
        }

        foreach (var pair in _collisionsToRemove)
        {
            _activeCollisions.Remove(pair);
            _collisionEvents.AddEnded(pair.ToCollision());
        }

        _collisionsToRemove.Clear();
    }

    // -------------------------------------------------------------------------
    // Body reconstruction and mutable settings
    //
    // Jitter shapes are rebuilt when collider geometry changes. Position,
    // rotation, and velocities are preserved across the replacement.
    // -------------------------------------------------------------------------

    private void RebuildBoxBody(
        Entity entity,
        BodyEntry oldEntry,
        in PhysicsBody3D settings,
        in BoxCollider3D collider)
    {
        var position = PhysicsConvert.ToMonoGame(oldEntry.Body.Position);
        var rotation = PhysicsConvert.ToMonoGame(oldEntry.Body.Orientation);
        var velocity = Vector3.Zero;
        var angularVelocity = Vector3.Zero;

        if (oldEntry.Body.MotionType != MotionType.Static)
        {
            velocity = PhysicsConvert.ToMonoGame(oldEntry.Body.Velocity);
            angularVelocity = PhysicsConvert.ToMonoGame(oldEntry.Body.AngularVelocity);
        }

        RemoveBody(entity);
        CreateBoxBody(entity, position, rotation, settings, collider);

        if (ToJitterMotionType(settings.MotionType) != MotionType.Static &&
            _bodies.TryGetValue(entity, out var newEntry))
        {
            newEntry.Body.Velocity = PhysicsConvert.ToJitter(velocity);
            newEntry.Body.AngularVelocity = PhysicsConvert.ToJitter(angularVelocity);
        }
    }

    private static void ApplySettings(
        RigidBody body,
        in PhysicsBody3D settings,
        in PhysicsBody3D previousSettings,
        bool forceMassUpdate = false)
    {
        settings.Validate();
        var motionType = ToJitterMotionType(settings.MotionType);
        if (body.MotionType != motionType)
        {
            body.MotionType = motionType;
        }

        body.Friction = settings.Friction;
        body.Restitution = settings.Restitution;
        body.AffectedByGravity = settings.AffectedByGravity;

        if (motionType == MotionType.Dynamic &&
            (forceMassUpdate || settings.Mass != previousSettings.Mass))
        {
            body.SetMassInertia(settings.Mass);
        }
    }

    // -------------------------------------------------------------------------
    // Internal body lookup and motion-type conversion
    // -------------------------------------------------------------------------

    private bool TryGetBody(Entity entity, out RigidBody body)
    {
        ThrowIfDisposed();
        if (_bodies.TryGetValue(entity, out var entry))
        {
            body = entry.Body;
            return true;
        }

        body = null!;
        return false;
    }

    private bool TryGetEntry(Entity entity, out BodyEntry entry)
    {
        ThrowIfDisposed();
        return _bodies.TryGetValue(entity, out entry!);
    }

    private static MotionType ToJitterMotionType(PhysicsMotionType motionType) =>
        motionType switch
        {
            PhysicsMotionType.Dynamic => MotionType.Dynamic,
            PhysicsMotionType.Kinematic => MotionType.Kinematic,
            PhysicsMotionType.Static => MotionType.Static,
            _ => throw new ArgumentOutOfRangeException(nameof(motionType))
        };

    // -------------------------------------------------------------------------
    // Kinematic rotation math and input validation
    // -------------------------------------------------------------------------

    private static Vector3 CalculateAngularVelocity(
        in Quaternion current,
        in Quaternion target,
        float deltaSeconds)
    {
        var adjustedTarget = target;
        if (Quaternion.Dot(current, adjustedTarget) < 0f)
        {
            adjustedTarget = new Quaternion(
                -adjustedTarget.X,
                -adjustedTarget.Y,
                -adjustedTarget.Z,
                -adjustedTarget.W);
        }

        var delta = Quaternion.Normalize(adjustedTarget * Quaternion.Inverse(current));
        var halfAngle = MathF.Acos(Math.Clamp(delta.W, -1f, 1f));
        var sinHalfAngle = MathF.Sin(halfAngle);
        if (MathF.Abs(sinHalfAngle) < 0.00001f)
        {
            return Vector3.Zero;
        }

        var axis = new Vector3(delta.X, delta.Y, delta.Z) / sinHalfAngle;
        return axis * (2f * halfAngle / deltaSeconds);
    }

    private static void ValidatePose(in Vector3 position, in Quaternion rotation)
    {
        if (!IsFinite(position))
        {
            throw new ArgumentException("Physics position must contain only finite values.", nameof(position));
        }

        if (!IsFinite(rotation) || rotation.LengthSquared() < 0.000001f)
        {
            throw new ArgumentException(
                "Physics rotation must be a finite, non-zero quaternion.",
                nameof(rotation));
        }
    }

    private static bool IsFinite(in Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static bool IsFinite(in Quaternion value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    // -------------------------------------------------------------------------
    // Internal storage types
    // -------------------------------------------------------------------------

    private sealed class BodyEntry
    {
        public BodyEntry(RigidBody body, PhysicsBody3D settings, Vector3 colliderSize)
        {
            Body = body;
            Settings = settings;
            ColliderSize = colliderSize;
        }

        public RigidBody Body { get; }
        public PhysicsBody3D Settings;
        public Vector3 ColliderSize { get; }
    }

    private readonly record struct CollisionPair(Entity First, Entity Second)
    {
        public static CollisionPair Create(Entity first, Entity second) =>
            ComesBefore(first, second)
                ? new CollisionPair(first, second)
                : new CollisionPair(second, first);

        public PhysicsCollision ToCollision() => new(First, Second);

        private static bool ComesBefore(Entity first, Entity second) =>
            first.Id < second.Id ||
            (first.Id == second.Id && first.Generation <= second.Generation);
    }
}

// Describes what EnsureBoxBody changed so PhysicsSystem can reset interpolation
// state only when a native body was created or replaced.
internal enum PhysicsBodyChange
{
    None,
    Created,
    Rebuilt
}

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
    // EnsureBody is the main synchronization entry point. It creates a missing
    // body, mutates or replaces its shape, and updates its settings. Shape
    // changes deliberately keep the same RigidBody instance.
    // -------------------------------------------------------------------------

    internal PhysicsBodyChange EnsureBody(
        Entity entity,
        in Transform3D transform,
        in PhysicsBody3D settings,
        in PhysicsColliderDefinition collider)
    {
        ThrowIfDisposed();
        settings.Validate();
        ValidatePose(transform.Position, transform.Rotation);

        if (!_bodies.TryGetValue(entity, out var entry))
        {
            CreateBody(entity, transform.Position, transform.Rotation, settings, collider);
            return PhysicsBodyChange.Created;
        }

        var shapeChanged = false;
        if (!ShapeMatches(entry.Shape, collider))
        {
            ReplaceShape(entity, entry, CreateShape(collider));
            shapeChanged = true;
        }
        else
        {
            shapeChanged = UpdateShapeGeometry(entry.Shape, collider);
            if (shapeChanged)
            {
                RemoveActiveCollisions(entity);
            }
        }

        ApplySettings(entry.Body, settings, entry.Settings, forceMassUpdate: shapeChanged);
        if (shapeChanged)
        {
            RefreshShapeInWorld(entry.Body);
        }

        entry.Settings = settings;
        return shapeChanged ? PhysicsBodyChange.ShapeChanged : PhysicsBodyChange.None;
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

        var settings = entry.Settings;
        settings.MotionType = motionType;
        ApplySettings(entry.Body, settings, entry.Settings);
        entry.Settings = settings;
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

    private void CreateBody(
        Entity entity,
        in Vector3 position,
        in Quaternion rotation,
        in PhysicsBody3D settings,
        in PhysicsColliderDefinition collider)
    {
        var body = _nativeWorld.CreateRigidBody();
        var shape = CreateShape(collider);
        try
        {
            body.Position = PhysicsConvert.ToJitter(position);
            body.Orientation = PhysicsConvert.ToJitter(Quaternion.Normalize(rotation));
            body.AddShape(shape);
            ApplySettings(body, settings, previousSettings: default, forceMassUpdate: true);

            _bodies.Add(entity, new BodyEntry(body, shape, settings));
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
    // Collider geometry and mutable body settings
    //
    // Primitive dimensions are mutable in Jitter. When the collider type
    // changes, only the attached shape is replaced. The RigidBody itself,
    // together with its velocity, identity, and constraints, stays alive.
    // -------------------------------------------------------------------------

    private void ReplaceShape(
        Entity entity,
        BodyEntry entry,
        RigidBodyShape replacement)
    {
        RemoveActiveCollisions(entity);
        entry.Body.RemoveShape(entry.Shape, MassInertiaUpdateMode.Preserve);
        try
        {
            entry.Body.AddShape(replacement, MassInertiaUpdateMode.Preserve);
        }
        catch
        {
            entry.Body.AddShape(entry.Shape, MassInertiaUpdateMode.Preserve);
            throw;
        }

        entry.Shape = replacement;
    }

    private static bool ShapeMatches(
        RigidBodyShape shape,
        in PhysicsColliderDefinition collider) =>
        collider.Kind switch
        {
            PhysicsColliderKind.Box => shape is BoxShape,
            PhysicsColliderKind.Sphere => shape is SphereShape,
            PhysicsColliderKind.Cylinder => shape is CylinderShape,
            PhysicsColliderKind.Capsule => shape is CapsuleShape,
            PhysicsColliderKind.Cone => shape is ConeShape,
            _ => throw new ArgumentOutOfRangeException(nameof(collider))
        };

    private static bool UpdateShapeGeometry(
        RigidBodyShape shape,
        in PhysicsColliderDefinition collider)
    {
        switch (shape)
        {
            case BoxShape box:
            {
                var currentSize = PhysicsConvert.ToMonoGame(box.Size);
                if (currentSize == collider.Size)
                {
                    return false;
                }

                box.Size = PhysicsConvert.ToJitter(collider.Size);
                return true;
            }

            case SphereShape sphere:
                if (sphere.Radius == collider.Radius)
                {
                    return false;
                }

                sphere.Radius = collider.Radius;
                return true;

            case CylinderShape cylinder:
                if (cylinder.Radius == collider.Radius &&
                    cylinder.Height == collider.Height)
                {
                    return false;
                }

                cylinder.Radius = collider.Radius;
                cylinder.Height = collider.Height;
                return true;

            case CapsuleShape capsule:
                if (capsule.Radius == collider.Radius &&
                    capsule.Length == collider.Length)
                {
                    return false;
                }

                capsule.Radius = collider.Radius;
                capsule.Length = collider.Length;
                return true;

            case ConeShape cone:
                if (cone.Radius == collider.Radius && cone.Height == collider.Height)
                {
                    return false;
                }

                cone.Radius = collider.Radius;
                cone.Height = collider.Height;
                return true;

            default:
                throw new ArgumentOutOfRangeException(nameof(collider));
        }
    }

    private static RigidBodyShape CreateShape(in PhysicsColliderDefinition collider) =>
        collider.Kind switch
        {
            PhysicsColliderKind.Box => new BoxShape(
                collider.Size.X,
                collider.Size.Y,
                collider.Size.Z),
            PhysicsColliderKind.Sphere => new SphereShape(collider.Radius),
            PhysicsColliderKind.Cylinder => new CylinderShape(
                collider.Height,
                collider.Radius),
            PhysicsColliderKind.Capsule => new CapsuleShape(
                collider.Radius,
                collider.Length),
            PhysicsColliderKind.Cone => new ConeShape(
                collider.Radius,
                collider.Height),
            _ => throw new ArgumentOutOfRangeException(nameof(collider))
        };

    private static void RefreshShapeInWorld(RigidBody body)
    {
        // Assigning the same position asks Jitter to refresh broad-phase data,
        // clear cached contacts, and wake affected bodies without replacing it.
        body.Position = body.Position;
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
            (forceMassUpdate ||
             previousSettings.MotionType != PhysicsMotionType.Dynamic ||
             settings.Mass != previousSettings.Mass))
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
        public BodyEntry(RigidBody body, RigidBodyShape shape, PhysicsBody3D settings)
        {
            Body = body;
            Shape = shape;
            Settings = settings;
        }

        public RigidBody Body { get; }
        public RigidBodyShape Shape;
        public PhysicsBody3D Settings;
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

// Describes what EnsureBody changed. Shape changes preserve the native body and
// therefore do not require interpolation history to be reset.
internal enum PhysicsBodyChange
{
    None,
    Created,
    ShapeChanged
}

internal enum PhysicsColliderKind
{
    Box,
    Sphere,
    Cylinder,
    Capsule,
    Cone
}

/// <summary>
/// A short-lived, engine-only description used to pass any supported ECS
/// collider to PhysicsSimulation without exposing Jitter types to the ECS.
/// </summary>
internal readonly record struct PhysicsColliderDefinition(
    PhysicsColliderKind Kind,
    Vector3 Size,
    float Radius,
    float Height,
    float Length)
{
    public static PhysicsColliderDefinition From(in BoxCollider3D collider) =>
        new(PhysicsColliderKind.Box, collider.Size, 0f, 0f, 0f);

    public static PhysicsColliderDefinition From(in SphereCollider3D collider) =>
        new(PhysicsColliderKind.Sphere, Vector3.Zero, collider.Radius, 0f, 0f);

    public static PhysicsColliderDefinition From(in CylinderCollider3D collider) =>
        new(PhysicsColliderKind.Cylinder, Vector3.Zero, collider.Radius, collider.Height, 0f);

    public static PhysicsColliderDefinition From(in CapsuleCollider3D collider) =>
        new(PhysicsColliderKind.Capsule, Vector3.Zero, collider.Radius, 0f, collider.Length);

    public static PhysicsColliderDefinition From(in ConeCollider3D collider) =>
        new(PhysicsColliderKind.Cone, Vector3.Zero, collider.Radius, collider.Height, 0f);
}

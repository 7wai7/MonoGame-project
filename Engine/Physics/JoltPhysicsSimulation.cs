using System;
using System.Collections.Generic;
using System.Diagnostics;
using JoltPhysicsSharp;
using Microsoft.Xna.Framework;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Diagnostics;
using MonoGame_project.Engine.Physics.Components;
using MonoGame_project.Engine.Rendering.ThreeD;
using MonoGame_project.Engine.Terrain;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;
using JoltSystem = JoltPhysicsSharp.PhysicsSystem;

namespace MonoGame_project.Engine.Physics;

/// <summary>Owns the Jolt world and keeps native physics objects outside ECS components.</summary>
internal sealed class JoltPhysicsSimulation : IDisposable
{
    private static readonly ObjectLayer StaticLayer = new(0);
    private static readonly ObjectLayer MovingLayer = new(1);

    private readonly ObjectLayerPairFilterTable _pairFilter;
    private readonly BroadPhaseLayerInterfaceTable _broadPhaseLayers;
    private readonly ObjectVsBroadPhaseLayerFilterTable _objectVsBroadPhase;
    private readonly JoltSystem _world;
    private readonly JobSystemThreadPool _jobs;
    private readonly BodyInterface _bodiesApi;
    private readonly PhysicsCollisionEvents _collisionEvents;
    private readonly Dictionary<Entity, BodyEntry> _bodies = [];
    private readonly Dictionary<Entity, TerrainBodyEntry> _terrainBodies = [];
    private readonly Dictionary<BodyID, Entity> _entitiesByBody = [];
    private readonly Dictionary<PhysicsCollision, int> _activeContacts = [];
    private readonly HashSet<PhysicsCollision> _publishedContacts = [];
    private readonly List<PhysicsCollision> _contactsToRemove = [];
    private readonly object _contactLock = new();
    private bool _disposed;

    public JoltPhysicsSimulation(PhysicsSettings settings, PhysicsCollisionEvents collisionEvents)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(collisionEvents);
        settings.Validate();
        _collisionEvents = collisionEvents;
        if (!Foundation.Init(false))
        {
            throw new InvalidOperationException("Jolt native initialization failed.");
        }

        ObjectLayerPairFilterTable? pairFilter = null;
        BroadPhaseLayerInterfaceTable? broadPhaseLayers = null;
        ObjectVsBroadPhaseLayerFilterTable? objectVsBroadPhase = null;
        JoltSystem? world = null;
        JobSystemThreadPool? jobs = null;
        try
        {
            pairFilter = new ObjectLayerPairFilterTable(2);
            pairFilter.EnableCollision(StaticLayer, MovingLayer);
            pairFilter.EnableCollision(MovingLayer, MovingLayer);
            broadPhaseLayers = new BroadPhaseLayerInterfaceTable(2, 2);
            broadPhaseLayers.MapObjectToBroadPhaseLayer(StaticLayer, new BroadPhaseLayer(0));
            broadPhaseLayers.MapObjectToBroadPhaseLayer(MovingLayer, new BroadPhaseLayer(1));
            objectVsBroadPhase = new ObjectVsBroadPhaseLayerFilterTable(
                broadPhaseLayers, 2, pairFilter, 2);
            world = new JoltSystem(new PhysicsSystemSettings
            {
                MaxBodies = 4_096,
                MaxBodyPairs = 16_384,
                MaxContactConstraints = 16_384,
                ObjectLayerPairFilter = pairFilter,
                BroadPhaseLayerInterface = broadPhaseLayers,
                ObjectVsBroadPhaseLayerFilter = objectVsBroadPhase
            });
            world.Gravity = ToNumerics(settings.Gravity);
            world.OnContactAdded += OnContactAdded;
            world.OnContactRemoved += OnContactRemoved;
            var jobConfig = new JobSystemThreadPoolConfig
            {
                maxJobs = 2_048,
                maxBarriers = 8,
                numThreads = settings.UseMultithreading ? -1 : 0
            };
            jobs = new JobSystemThreadPool(in jobConfig);

            _pairFilter = pairFilter;
            _broadPhaseLayers = broadPhaseLayers;
            _objectVsBroadPhase = objectVsBroadPhase;
            _world = world;
            _jobs = jobs;
            _bodiesApi = world.BodyInterface;
        }
        catch
        {
            jobs?.Dispose();
            world?.Dispose();
            objectVsBroadPhase?.Dispose();
            broadPhaseLayers?.Dispose();
            pairFilter?.Dispose();
            Foundation.Shutdown();
            throw;
        }
    }

    public int BodyCount => _bodies.Count + _terrainBodies.Count;
    public int TerrainBodyCount => _terrainBodies.Count;

    // ---------------------------------------------------------------------
    // ECS synchronization and body lifetime
    // ---------------------------------------------------------------------

    public PhysicsBodyChange EnsureBody(Entity entity, in Transform3D transform,
        in PhysicsBody3D settings, in PhysicsColliderDefinition collider)
    {
        ThrowIfDisposed();
        settings.Validate();
        ValidatePose(transform.Position, transform.Rotation);
        if (!_bodies.TryGetValue(entity, out var entry))
        {
            using var shape = CreateShape(collider);
            var position = ToNumerics(transform.Position);
            var rotation = ToNumerics(Quaternion.Normalize(transform.Rotation));
            using var creation = new BodyCreationSettings(shape, in position, in rotation,
                ToJoltMotionType(settings.MotionType), LayerFor(settings.MotionType))
            {
                Friction = settings.Friction,
                Restitution = settings.Restitution,
                GravityFactor = settings.AffectedByGravity ? 1f : 0f,
                AllowDynamicOrKinematic = true
            };
            if (settings.MotionType == PhysicsMotionType.Dynamic)
            {
                creation.OverrideMassProperties = OverrideMassProperties.CalculateInertia;
                creation.MassPropertiesOverride = new MassProperties { Mass = settings.Mass };
            }

            var id = _bodiesApi.CreateAndAddBody(creation,
                settings.MotionType == PhysicsMotionType.Static
                    ? Activation.DontActivate : Activation.Activate);
            if (!id.IsValid)
            {
                throw new InvalidOperationException($"Jolt could not create {entity}.");
            }

            _bodies.Add(entity, new BodyEntry(id, collider, settings));
            _entitiesByBody.Add(id, entity);
            return PhysicsBodyChange.Created;
        }

        var changed = entry.Collider != collider;
        if (changed)
        {
            using var shape = CreateShape(collider);
            var id = entry.Id;
            _bodiesApi.SetShape(in id, in shape, true, Activation.Activate);
            entry.Collider = collider;
            RemoveActiveContacts(entity);
        }

        ApplySettings(entry, settings, changed);
        return changed ? PhysicsBodyChange.ShapeChanged : PhysicsBodyChange.None;
    }

    public void EnsureTerrainBody(Entity entity, in Transform3D transform,
        in TerrainCollider3D collider)
    {
        ThrowIfDisposed();
        collider.Validate();
        ValidatePose(transform.Position, transform.Rotation);
        if (transform.Scale != Vector3.One ||
            MathF.Abs(Quaternion.Dot(Quaternion.Normalize(transform.Rotation), Quaternion.Identity)) < 0.99999f)
        {
            throw new InvalidOperationException($"{entity} terrain must be upright and have unit scale.");
        }

        if (_terrainBodies.TryGetValue(entity, out var existing) &&
            !ReferenceEquals(existing.Heights, collider.Heights))
        {
            RemoveBody(entity);
            existing = null;
        }

        if (existing is not null)
        {
            var id = existing.Id;
            var position = ToNumerics(transform.Position);
            if (_bodiesApi.GetPosition(in id) != position)
            {
                _bodiesApi.SetPosition(in id, in position, Activation.DontActivate);
            }

            if (existing.Friction != collider.Friction)
            {
                _bodiesApi.SetFriction(in id, collider.Friction);
                existing.Friction = collider.Friction;
            }

            return;
        }

        CreateTerrainBody(entity, transform.Position, collider);
    }

    public bool TryGetTerrainSurface(Entity entity, out TerrainSurface surface)
    {
        if (_terrainBodies.TryGetValue(entity, out var entry))
        {
            surface = entry.DebugSurface ??=
                TerrainSurfaceBuilder.CreateAdaptive(entry.Heights, 0f);
            return true;
        }

        surface = null!;
        return false;
    }

    public bool RemoveBody(Entity entity)
    {
        ThrowIfDisposed();
        BodyID id;
        if (_bodies.Remove(entity, out var body))
        {
            id = body.Id;
        }
        else if (_terrainBodies.Remove(entity, out var terrain))
        {
            id = terrain.Id;
        }
        else
        {
            return false;
        }

        RemoveActiveContacts(entity);
        _entitiesByBody.Remove(id);
        _bodiesApi.RemoveAndDestroyBody(in id);
        return true;
    }

    public bool TryGetPose(Entity entity, out Vector3 position, out Quaternion rotation)
    {
        if (_bodies.TryGetValue(entity, out var entry))
        {
            var id = entry.Id;
            position = ToMonoGame(_bodiesApi.GetPosition(in id));
            rotation = ToMonoGame(_bodiesApi.GetRotation(in id));
            return true;
        }

        position = Vector3.Zero;
        rotation = Quaternion.Identity;
        return false;
    }

    // ---------------------------------------------------------------------
    // Commands and fixed step
    // ---------------------------------------------------------------------

    public bool ApplyForce(Entity entity, in Vector3 force)
    {
        if (!TryGetMovingBody(entity, out var id)) return false;
        var value = ToNumerics(force);
        _bodiesApi.AddForce(in id, in value);
        return true;
    }

    public bool ApplyTorque(Entity entity, in Vector3 torque)
    {
        if (!TryGetMovingBody(entity, out var id)) return false;
        var value = ToNumerics(torque);
        _bodiesApi.AddTorque(in id, in value);
        return true;
    }

    public bool ApplyImpulse(Entity entity, in Vector3 impulse)
    {
        if (!TryGetMovingBody(entity, out var id)) return false;
        var value = ToNumerics(impulse);
        _bodiesApi.AddImpulse(in id, in value);
        return true;
    }

    public bool ApplyImpulse(Entity entity, in Vector3 impulse, in Vector3 worldPosition)
    {
        if (!TryGetMovingBody(entity, out var id)) return false;
        var value = ToNumerics(impulse);
        var position = ToNumerics(worldPosition);
        _bodiesApi.AddImpulse(in id, in value, in position);
        return true;
    }

    public bool SetVelocity(Entity entity, in Vector3 velocity)
    {
        if (!TryGetMovingBody(entity, out var id)) return false;
        var value = ToNumerics(velocity);
        _bodiesApi.SetLinearVelocity(in id, in value);
        return true;
    }

    public bool SetMotionType(Entity entity, PhysicsMotionType motionType)
    {
        if (!_bodies.TryGetValue(entity, out var entry)) return false;
        var settings = entry.Settings;
        settings.MotionType = motionType;
        ApplySettings(entry, settings, shapeChanged: false);
        return true;
    }

    public bool SetKinematicTarget(Entity entity, in Vector3 position,
        in Quaternion rotation, float deltaSeconds)
    {
        if (!_bodies.TryGetValue(entity, out var entry)) return false;
        if (entry.Settings.MotionType != PhysicsMotionType.Kinematic)
            throw new InvalidOperationException($"{entity} is not kinematic.");
        ValidatePose(position, rotation);
        if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));

        var id = entry.Id;
        var targetPosition = ToNumerics(position);
        var targetRotation = ToNumerics(Quaternion.Normalize(rotation));
        _bodiesApi.MoveKinematic(in id, in targetPosition, in targetRotation, deltaSeconds);
        return true;
    }

    public bool StopKinematic(Entity entity)
    {
        if (!_bodies.TryGetValue(entity, out var entry)) return false;
        if (entry.Settings.MotionType == PhysicsMotionType.Kinematic)
        {
            var id = entry.Id;
            var zero = NVector3.Zero;
            _bodiesApi.SetLinearAndAngularVelocity(in id, in zero, in zero);
        }

        return true;
    }

    public bool Teleport(Entity entity, in Vector3 position, in Quaternion rotation)
    {
        if (!_bodies.TryGetValue(entity, out var entry)) return false;
        ValidatePose(position, rotation);
        var id = entry.Id;
        var targetPosition = ToNumerics(position);
        var targetRotation = ToNumerics(Quaternion.Normalize(rotation));
        _bodiesApi.SetPositionAndRotation(in id, in targetPosition, in targetRotation,
            entry.Settings.MotionType == PhysicsMotionType.Static
                ? Activation.DontActivate : Activation.Activate);
        _bodiesApi.InvalidateContactCache(in id);
        if (entry.Settings.MotionType != PhysicsMotionType.Static)
        {
            var zero = NVector3.Zero;
            _bodiesApi.SetLinearAndAngularVelocity(in id, in zero, in zero);
        }

        return true;
    }

    public void Step(float deltaSeconds)
    {
        ThrowIfDisposed();
        var error = _world.Update(deltaSeconds, 1, _jobs);
        if (error != PhysicsUpdateError.None)
            throw new InvalidOperationException($"Jolt physics step failed: {error}.");

        // Several terrain sub-shapes may exchange contacts during one step.
        // Publish only the resulting entity-pair state, not each sub-shape contact.
        lock (_contactLock)
        {
            foreach (var pair in _activeContacts.Keys)
                if (_publishedContacts.Add(pair))
                    _collisionEvents.AddStarted(pair);

            _contactsToRemove.Clear();
            foreach (var pair in _publishedContacts)
                if (!_activeContacts.ContainsKey(pair))
                    _contactsToRemove.Add(pair);
            foreach (var pair in _contactsToRemove)
            {
                _publishedContacts.Remove(pair);
                _collisionEvents.AddEnded(pair);
            }

            _contactsToRemove.Clear();
        }
    }

    // ---------------------------------------------------------------------
    // Native construction, geometry, and settings
    // ---------------------------------------------------------------------

    private void CreateTerrainBody(Entity entity, in Vector3 position,
        in TerrainCollider3D collider)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var heights = collider.Heights;
        var count = heights.CellCount + 1;
        var samples = new float[count * count];
        for (var z = 0; z < count; z++)
            for (var x = 0; x < count; x++)
                samples[z * count + x] = heights.GetHeight(x, z);

        var localOffset = NVector3.Zero;
        var scale = new NVector3(heights.CellSize, 1f, heights.CellSize);
        using var shapeSettings = new HeightFieldShapeSettings(samples, in localOffset, in scale,
            (uint)count)
        {
            BitsPerSample = 16
        };
        using var shape = new HeightFieldShape(in shapeSettings);
        if (shape.SampleCount < count)
            throw new InvalidOperationException("Jolt heightfield contains fewer samples than the terrain chunk.");
        for (var padded = (uint)count; padded < shape.SampleCount; padded++)
            for (uint sample = 0; sample < shape.SampleCount; sample++)
                if (!shape.IsNoCollision(padded, sample) ||
                    !shape.IsNoCollision(sample, padded))
                    throw new InvalidOperationException(
                        "Jolt heightfield padding would create a terrain seam.");

        var worldPosition = ToNumerics(position);
        var identity = NQuaternion.Identity;
        using var creation = new BodyCreationSettings(shape, in worldPosition, in identity,
            MotionType.Static, StaticLayer)
        {
            Friction = collider.Friction,
            GravityFactor = 0f
        };
        var id = _bodiesApi.CreateAndAddBody(creation, Activation.DontActivate);
        if (!id.IsValid)
            throw new InvalidOperationException($"Jolt could not create terrain body {entity}.");

        _terrainBodies.Add(entity, new TerrainBodyEntry(id, heights, collider.Friction));
        _entitiesByBody.Add(id, entity);
        var duration = Stopwatch.GetElapsedTime(startedAt);
        var details = $"chunk ({heights.ChunkX}, {heights.ChunkZ}), " +
            $"{count}x{count} samples, {shape.SampleCount} padded samples/axis";
        if (StartupProfiler.IsActive)
            StartupProfiler.Record("Jolt heightfield body setup", duration, details);
        else
            Console.WriteLine($"[terrain-collision] Jolt {details}: setup " +
                $"{duration.TotalSeconds:F3}s, active terrain bodies {_terrainBodies.Count}, " +
                $"{ProcessMemorySnapshot.Capture()}.");
    }

    private static Shape CreateShape(in PhysicsColliderDefinition collider)
    {
        switch (collider.Kind)
        {
            case PhysicsColliderKind.Box:
                var halfExtent = ToNumerics(collider.Size * 0.5f);
                return new BoxShape(in halfExtent, 0f);
            case PhysicsColliderKind.Sphere:
                return new SphereShape(collider.Radius);
            case PhysicsColliderKind.Cylinder:
                return new CylinderShape(collider.Height * 0.5f, collider.Radius);
            case PhysicsColliderKind.Capsule:
                return new CapsuleShape(collider.Length * 0.5f, collider.Radius);
            case PhysicsColliderKind.Cone:
                using (var coneSettings = new TaperedCylinderShapeSettings(
                    collider.Height * 0.5f, 0f, collider.Radius, 0f, null!))
                using (var cone = new TaperedCylinderShape(coneSettings))
                {
                    // Keep the existing ECS cone origin one quarter of its
                    // height above the base, matching the render mesh.
                    var offset = new NVector3(0f, collider.Height * 0.25f, 0f);
                    var rotation = NQuaternion.Identity;
                    return new RotatedTranslatedShape(in offset, in rotation, cone);
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(collider));
        }
    }

    private void ApplySettings(BodyEntry entry, in PhysicsBody3D settings, bool shapeChanged)
    {
        settings.Validate();
        var id = entry.Id;
        var changedMotion = settings.MotionType != entry.Settings.MotionType;
        if (changedMotion)
        {
            _bodiesApi.SetMotionType(in id, ToJoltMotionType(settings.MotionType),
                settings.MotionType == PhysicsMotionType.Static
                    ? Activation.DontActivate : Activation.Activate);
            var layer = LayerFor(settings.MotionType);
            _bodiesApi.SetObjectLayer(in id, in layer);
        }

        if (settings.Friction != entry.Settings.Friction)
            _bodiesApi.SetFriction(in id, settings.Friction);
        if (settings.Restitution != entry.Settings.Restitution)
            _bodiesApi.SetRestitution(in id, settings.Restitution);
        if (settings.AffectedByGravity != entry.Settings.AffectedByGravity)
            _bodiesApi.SetGravityFactor(in id, settings.AffectedByGravity ? 1f : 0f);
        if (settings.MotionType == PhysicsMotionType.Dynamic &&
            (shapeChanged || changedMotion || settings.Mass != entry.Settings.Mass))
        {
            var locking = _world.BodyLockInterface;
            var bodyLock = default(BodyLockWrite);
            locking.LockWrite(in id, out bodyLock);
            try
            {
                if (!bodyLock.Succeeded)
                    throw new InvalidOperationException("Could not lock Jolt body to update mass.");
                bodyLock.Body!.MotionProperties.ScaleToMass(settings.Mass);
            }
            finally
            {
                locking.UnlockWrite(in bodyLock);
            }
        }

        entry.Settings = settings;
    }

    private bool TryGetMovingBody(Entity entity, out BodyID id)
    {
        ThrowIfDisposed();
        if (_bodies.TryGetValue(entity, out var entry) &&
            entry.Settings.MotionType != PhysicsMotionType.Static)
        {
            id = entry.Id;
            return true;
        }

        id = BodyID.Invalid;
        return false;
    }

    // ---------------------------------------------------------------------
    // Contact events and cleanup
    // ---------------------------------------------------------------------

    private void OnContactAdded(JoltSystem system, in Body first, in Body second,
        in ContactManifold manifold, ref ContactSettings settings)
    {
        lock (_contactLock)
        {
            if (!TryCreatePair(first.ID, second.ID, out var pair)) return;
            if (_activeContacts.TryGetValue(pair, out var count))
                _activeContacts[pair] = count + 1;
            else
                _activeContacts.Add(pair, 1);
        }
    }

    private void OnContactRemoved(JoltSystem system, ref SubShapeIDPair subShapes)
    {
        lock (_contactLock)
        {
            if (!TryCreatePair(subShapes.Body1ID, subShapes.Body2ID, out var pair) ||
                !_activeContacts.TryGetValue(pair, out var count)) return;
            if (count > 1)
                _activeContacts[pair] = count - 1;
            else
                _activeContacts.Remove(pair);
        }
    }

    private bool TryCreatePair(BodyID firstId, BodyID secondId, out PhysicsCollision pair)
    {
        if (_entitiesByBody.TryGetValue(firstId, out var first) &&
            _entitiesByBody.TryGetValue(secondId, out var second))
        {
            pair = first.Id < second.Id ||
                   (first.Id == second.Id && first.Generation <= second.Generation)
                ? new PhysicsCollision(first, second)
                : new PhysicsCollision(second, first);
            return true;
        }

        pair = default;
        return false;
    }

    private void RemoveActiveContacts(Entity entity)
    {
        lock (_contactLock)
        {
            _contactsToRemove.Clear();
            foreach (var pair in _activeContacts.Keys)
                if (pair.First == entity || pair.Second == entity)
                    _contactsToRemove.Add(pair);
            foreach (var pair in _contactsToRemove)
            {
                _activeContacts.Remove(pair);
                if (_publishedContacts.Remove(pair))
                    _collisionEvents.AddEnded(pair);
            }

            _contactsToRemove.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var entity in new List<Entity>(_bodies.Keys)) RemoveBody(entity);
        foreach (var entity in new List<Entity>(_terrainBodies.Keys)) RemoveBody(entity);
        _world.OnContactAdded -= OnContactAdded;
        _world.OnContactRemoved -= OnContactRemoved;
        _jobs.Dispose();
        _world.Dispose();
        _objectVsBroadPhase.Dispose();
        _broadPhaseLayers.Dispose();
        _pairFilter.Dispose();
        Foundation.Shutdown();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(JoltPhysicsSimulation));
    }

    private static MotionType ToJoltMotionType(PhysicsMotionType value) => value switch
    {
        PhysicsMotionType.Static => MotionType.Static,
        PhysicsMotionType.Kinematic => MotionType.Kinematic,
        PhysicsMotionType.Dynamic => MotionType.Dynamic,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static ObjectLayer LayerFor(PhysicsMotionType value) =>
        value == PhysicsMotionType.Static ? StaticLayer : MovingLayer;

    private static NVector3 ToNumerics(in Vector3 value) => new(value.X, value.Y, value.Z);
    private static NQuaternion ToNumerics(in Quaternion value) =>
        new(value.X, value.Y, value.Z, value.W);
    private static Vector3 ToMonoGame(in NVector3 value) => new(value.X, value.Y, value.Z);
    private static Quaternion ToMonoGame(in NQuaternion value) =>
        new(value.X, value.Y, value.Z, value.W);

    private static void ValidatePose(in Vector3 position, in Quaternion rotation)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) ||
            !float.IsFinite(position.Z) || !float.IsFinite(rotation.X) ||
            !float.IsFinite(rotation.Y) || !float.IsFinite(rotation.Z) ||
            !float.IsFinite(rotation.W) || rotation.LengthSquared() < 1e-12f)
        {
            throw new ArgumentOutOfRangeException(nameof(position),
                "Physics pose must contain finite position and nonzero rotation.");
        }
    }

    private sealed class BodyEntry(BodyID id, PhysicsColliderDefinition collider,
        PhysicsBody3D settings)
    {
        public BodyID Id { get; } = id;
        public PhysicsColliderDefinition Collider = collider;
        public PhysicsBody3D Settings = settings;
    }

    private sealed class TerrainBodyEntry(BodyID id, TerrainHeightField heights, float friction)
    {
        public BodyID Id { get; } = id;
        public TerrainHeightField Heights { get; } = heights;
        public float Friction = friction;
        public TerrainSurface? DebugSurface;
    }
}

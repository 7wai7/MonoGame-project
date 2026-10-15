using System.Collections.Generic;
using MonoGame_project.Ecs;

namespace MonoGame_project.Engine.Physics;

public readonly record struct PhysicsCollision(Entity First, Entity Second);

/// <summary>Collision changes produced by the most recent fixed physics step.</summary>
public sealed class PhysicsCollisionEvents
{
    private readonly List<PhysicsCollision> _started = [];
    private readonly List<PhysicsCollision> _ended = [];
    private readonly List<PhysicsCollision> _pendingStarted = [];
    private readonly List<PhysicsCollision> _pendingEnded = [];

    public IReadOnlyList<PhysicsCollision> Started => _started;
    public IReadOnlyList<PhysicsCollision> Ended => _ended;

    internal void Publish()
    {
        _started.Clear();
        _ended.Clear();
        _started.AddRange(_pendingStarted);
        _ended.AddRange(_pendingEnded);
        _pendingStarted.Clear();
        _pendingEnded.Clear();
    }

    internal void AddStarted(in PhysicsCollision collision) => _pendingStarted.Add(collision);
    internal void AddEnded(in PhysicsCollision collision) => _pendingEnded.Add(collision);

    internal void Clear()
    {
        _started.Clear();
        _ended.Clear();
        _pendingStarted.Clear();
        _pendingEnded.Clear();
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Diagnostics;
using MonoGame_project.Engine.Physics;
using MonoGame_project.Engine.Physics.Components;
using MonoGame_project.Engine.Rendering.ThreeD;
using MonoGame_project.Engine.Systems;
using MonoGame_project.Engine.Terrain;
using MonoGame_project.Engine.Time;

namespace MonoGame_project.Game.Terrain;

internal readonly record struct TerrainChunk(Entity Entity, TerrainHeightField Heights)
{
    public int X => Heights.ChunkX;
    public int Z => Heights.ChunkZ;
}

/// <summary>
/// Keeps terrain collision near the active camera and moving physics bodies.
/// Render geometry streams independently from the static collision neighborhood.
/// </summary>
internal sealed class TerrainColliderStreamingSystem : GameSystem, IUpdateSystem
{
    private readonly IReadOnlyList<TerrainChunk> _chunks;
    private readonly float _chunkWorldSize;
    private readonly int _radius;
    private readonly HashSet<Point> _wanted = [];
    private int _firstChunkX;
    private int _lastChunkX;
    private int _firstChunkZ;
    private int _lastChunkZ;

    public TerrainColliderStreamingSystem(
        IReadOnlyList<TerrainChunk> chunks,
        float chunkWorldSize,
        int radius)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        if (!float.IsFinite(chunkWorldSize) || chunkWorldSize <= 0f ||
            radius < 0)
        {
            throw new ArgumentException("Terrain collision streaming settings are invalid.");
        }

        _chunks = chunks;
        _chunkWorldSize = chunkWorldSize;
        _radius = radius;
    }

    protected override void OnInitialize()
    {
        if (_chunks.Count == 0)
        {
            throw new InvalidOperationException("Terrain chunks must exist before collision streaming starts.");
        }

        _firstChunkX = int.MaxValue;
        _firstChunkZ = int.MaxValue;
        _lastChunkX = int.MinValue;
        _lastChunkZ = int.MinValue;
        foreach (var chunk in _chunks)
        {
            _firstChunkX = Math.Min(_firstChunkX, chunk.X);
            _lastChunkX = Math.Max(_lastChunkX, chunk.X);
            _firstChunkZ = Math.Min(_firstChunkZ, chunk.Z);
            _lastChunkZ = Math.Max(_lastChunkZ, chunk.Z);
        }

        RefreshColliders();
    }

    public void Update(World world, in FrameTime time) => RefreshColliders();

    private void RefreshColliders()
    {
        var startedAt = Stopwatch.GetTimestamp();
        var added = 0;
        var removed = 0;
        _wanted.Clear();
        foreach (var entity in World.Query<Transform3D, Camera3D>())
        {
            if (World.Get<Camera3D>(entity).IsActive)
            {
                AddNeighborhood(World.Get<Transform3D>(entity).Position);
            }
        }

        // Keeping collision around independent moving bodies means they still
        // have ground under them when the inspect camera flies elsewhere.
        foreach (var entity in World.Query<Transform3D, PhysicsBody3D>())
        {
            if (World.Get<PhysicsBody3D>(entity).MotionType != PhysicsMotionType.Static)
            {
                AddNeighborhood(World.Get<Transform3D>(entity).Position);
            }
        }

        foreach (var chunk in _chunks)
        {
            var shouldHaveCollider = _wanted.Contains(new Point(chunk.X, chunk.Z));
            var hasCollider = World.Has<TerrainCollider3D>(chunk.Entity);
            if (shouldHaveCollider && !hasCollider)
            {
                World.Add(chunk.Entity, new TerrainCollider3D(chunk.Heights));
                added++;
            }
            else if (!shouldHaveCollider && hasCollider)
            {
                World.Remove<TerrainCollider3D>(chunk.Entity);
                removed++;
            }
        }

        if ((added != 0 || removed != 0) && !StartupProfiler.IsActive)
        {
            Console.WriteLine(
                $"[terrain-collision-stream] added {added}, removed {removed} " +
                $"collider components; {_wanted.Count} wanted chunks; " +
                $"neighborhood update {Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F2}ms.");
        }
    }

    private void AddNeighborhood(in Vector3 position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Z))
        {
            return;
        }

        var centerX = Math.Clamp((int)MathF.Floor(position.X / _chunkWorldSize),
            _firstChunkX, _lastChunkX);
        var centerZ = Math.Clamp((int)MathF.Floor(position.Z / _chunkWorldSize),
            _firstChunkZ, _lastChunkZ);
        for (var z = Math.Max(_firstChunkZ, centerZ - _radius);
            z <= Math.Min(_lastChunkZ, centerZ + _radius); z++)
        {
            for (var x = Math.Max(_firstChunkX, centerX - _radius);
                x <= Math.Min(_lastChunkX, centerX + _radius); x++)
            {
                _wanted.Add(new Point(x, z));
            }
        }
    }
}

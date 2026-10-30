using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Diagnostics;
using MonoGame_project.Engine.Rendering.ThreeD;
using MonoGame_project.Engine.Systems;
using MonoGame_project.Engine.Terrain;
using MonoGame_project.Engine.Time;

namespace MonoGame_project.Game.Terrain;

/// <summary>
/// Builds visible terrain geometry on a worker, then installs GPU buffers and ECS
/// renderers on the game thread. The shared full-resolution chunk border keeps
/// neighboring LODs joined without modifying the independent collision mesh.
/// </summary>
internal sealed class TerrainRenderStreamingSystem : GameSystem, IContentSystem, IUpdateSystem
{
    private const int InitialChunkRadius = 1;
    private const float NearChunkRadius = 3f;
    private const float MiddleChunkRadius = 9f;
    private const float LodHysteresis = 0.5f;

    private readonly IReadOnlyList<TerrainChunk> _chunks;
    private readonly float _chunkWorldSize;
    private readonly float _middleMaxHeightError;
    private readonly float _farMaxHeightError;
    private readonly int _farMaxPatchCellSpan;
    private readonly Vector2 _textureWorldOrigin;
    private readonly Vector2 _textureWorldSize;
    private readonly List<ChunkState> _states = [];
    private CancellationTokenSource? _cancellation;
    private Task<MeshData>? _buildTask;
    private ChunkState? _buildingChunk;
    private RenderLod _buildingLod;
    private long _streamStartedAt;
    private int _loadedChunkCount;
    private bool _contentLoaded;

    public TerrainRenderStreamingSystem(
        IReadOnlyList<TerrainChunk> chunks,
        float chunkWorldSize,
        float middleMaxHeightError,
        float farMaxHeightError,
        int farMaxPatchCellSpan,
        Vector2 textureWorldOrigin,
        Vector2 textureWorldSize)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        if (!float.IsFinite(chunkWorldSize) || chunkWorldSize <= 0f ||
            !float.IsFinite(middleMaxHeightError) || middleMaxHeightError < 0f ||
            !float.IsFinite(farMaxHeightError) || farMaxHeightError < middleMaxHeightError ||
            farMaxPatchCellSpan < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(farMaxHeightError));
        }

        _chunks = chunks;
        _chunkWorldSize = chunkWorldSize;
        _middleMaxHeightError = middleMaxHeightError;
        _farMaxHeightError = farMaxHeightError;
        _farMaxPatchCellSpan = farMaxPatchCellSpan;
        _textureWorldOrigin = textureWorldOrigin;
        _textureWorldSize = textureWorldSize;
    }

    protected override void OnInitialize()
    {
        foreach (var chunk in _chunks)
        {
            var heights = chunk.Heights;
            var size = heights.CellCount * heights.CellSize;
            var bounds = new BoundingBox(
                new Vector3(heights.OriginX, heights.MinHeight - _farMaxHeightError,
                    heights.OriginZ),
                new Vector3(heights.OriginX + size, heights.MaxHeight + _farMaxHeightError,
                    heights.OriginZ + size));
            _states.Add(new ChunkState(chunk, bounds));
        }
    }

    public void LoadContent(World world, ContentManager content)
    {
        _cancellation = new CancellationTokenSource();
        _streamStartedAt = Stopwatch.GetTimestamp();
        _loadedChunkCount = 0;
        _contentLoaded = true;
        try
        {
            LoadInitialChunks();
        }
        catch
        {
            UnloadContent(world);
            throw;
        }
    }

    private void LoadInitialChunks()
    {
        // Give the first frame exact ground near the camera and the starting
        // physics bodies. Other visible chunks arrive in the background.
        if (!MeshRenderSystem.TryGetActiveCamera(World, out var cameraTransform, out _))
        {
            return;
        }

        var centerX = (int)MathF.Floor(cameraTransform.Position.X / _chunkWorldSize);
        var centerZ = (int)MathF.Floor(cameraTransform.Position.Z / _chunkWorldSize);
        var initialCount = 0;
        using (StartupProfiler.Measure("Initial nearby full-resolution terrain meshes"))
        {
            foreach (var state in _states)
            {
                if (Math.Abs(state.Chunk.X - centerX) > InitialChunkRadius ||
                    Math.Abs(state.Chunk.Z - centerZ) > InitialChunkRadius)
                {
                    continue;
                }

                Install(state, RenderLod.Near, BuildMeshData(state, RenderLod.Near));
                initialCount++;
            }
        }

        StartupProfiler.Info(
            $"Initial terrain meshes: {initialCount}; remaining visible chunks stream after first frame.");
    }

    public void Update(World world, in FrameTime time)
    {
        if (!_contentLoaded ||
            !MeshRenderSystem.TryGetActiveCamera(world, out var cameraTransform, out var camera))
        {
            return;
        }

        // A completed CPU mesh is uploaded only here, never on the worker.
        // One in-flight job also bounds temporary mesh memory and upload work.
        if (_buildTask is { IsCompleted: true })
        {
            var task = _buildTask;
            var state = _buildingChunk!;
            _buildTask = null;
            _buildingChunk = null;
            if (!task.IsCanceled)
            {
                Install(state, _buildingLod, task.GetAwaiter().GetResult());
            }
        }

        if (_buildTask is not null)
        {
            return;
        }

        var view = Matrix.CreateLookAt(cameraTransform.Position, camera.Target, camera.Up);
        var projection = Matrix.CreatePerspectiveFieldOfView(camera.FieldOfView,
            Context.GraphicsDevice.Viewport.AspectRatio, camera.NearPlane, camera.FarPlane);
        var frustum = new BoundingFrustum(view * projection);
        if (!TryFindNext(cameraTransform.Position, frustum, out var next, out var lod))
        {
            return;
        }

        var token = _cancellation!.Token;
        _buildingChunk = next;
        _buildingLod = lod;
        _buildTask = Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var data = BuildMeshData(next, lod);
            token.ThrowIfCancellationRequested();
            return data;
        }, token);
    }

    public void UnloadContent(World world)
    {
        if (!_contentLoaded)
        {
            return;
        }

        _contentLoaded = false;
        _cancellation?.Cancel();
        if (_buildTask is not null)
        {
            // Do not block scene unloading on a CPU-only task, but observe faults.
            _ = _buildTask.ContinueWith(task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        _buildTask = null;
        _buildingChunk = null;
        _cancellation?.Dispose();
        _cancellation = null;
        foreach (var state in _states)
        {
            if (World.IsAlive(state.Chunk.Entity))
            {
                World.Remove<MeshRenderer>(state.Chunk.Entity);
            }

            state.Mesh?.Dispose();
            state.Mesh = null;
        }

        _loadedChunkCount = 0;
    }

    private bool TryFindNext(
        in Vector3 cameraPosition,
        BoundingFrustum frustum,
        out ChunkState next,
        out RenderLod lod)
    {
        ChunkState? best = null;
        var bestPriority = float.PositiveInfinity;
        var bestLod = RenderLod.Far;
        foreach (var state in _states)
        {
            var distance = DistanceToChunk(cameraPosition, state.Bounds);
            var visible = frustum.Intersects(state.Bounds);
            var nearCamera = distance < 2f * _chunkWorldSize;
            if (!visible && !nearCamera && state.Mesh is null)
            {
                continue;
            }

            var wantedLod = SelectLod(distance, state.Lod);
            if (state.Mesh is not null && state.Lod == wantedLod)
            {
                continue;
            }

            // Near ground is built exactly, matching the collision heightfield.
            // Missing middle/far chunks get a coarse surface before upgrades.
            var buildLod = state.Mesh is null && wantedLod != RenderLod.Near
                ? RenderLod.Far : wantedLod;
            if (!visible && !nearCamera && buildLod != RenderLod.Far)
            {
                continue;
            }

            var priority = distance * distance;
            if (state.Mesh is not null)
            {
                priority += buildLod < state.Lod
                    ? 4f * _chunkWorldSize * _chunkWorldSize
                    : 1_000_000_000f;
            }

            if (!visible && !nearCamera)
            {
                priority += 2_000_000_000f;
            }

            if (priority >= bestPriority)
            {
                continue;
            }

            best = state;
            bestLod = buildLod;
            bestPriority = priority;
        }

        next = best!;
        lod = bestLod;
        return best is not null;
    }

    private RenderLod SelectLod(float distance, RenderLod current)
    {
        var near = NearChunkRadius * _chunkWorldSize;
        var middle = MiddleChunkRadius * _chunkWorldSize;
        var hysteresis = LodHysteresis * _chunkWorldSize;
        if (current == RenderLod.Near && distance < near + hysteresis)
        {
            return RenderLod.Near;
        }

        if (current == RenderLod.Middle &&
            distance >= near - hysteresis && distance < middle + hysteresis)
        {
            return RenderLod.Middle;
        }

        if (current == RenderLod.Far && distance >= middle - hysteresis)
        {
            return RenderLod.Far;
        }

        return distance < near ? RenderLod.Near :
            distance < middle ? RenderLod.Middle : RenderLod.Far;
    }

    private MeshData BuildMeshData(ChunkState state, RenderLod lod) => lod switch
    {
        RenderLod.Near => TerrainMeshFactory.CreateFullResolution(
            state.Chunk.Heights, _textureWorldOrigin, _textureWorldSize),
        RenderLod.Middle => TerrainMeshFactory.Create(
            state.Chunk.Heights, _middleMaxHeightError,
            _textureWorldOrigin, _textureWorldSize),
        _ => TerrainMeshFactory.Create(
            state.Chunk.Heights, _farMaxHeightError,
            _textureWorldOrigin, _textureWorldSize, _farMaxPatchCellSpan)
    };

    private void Install(ChunkState state, RenderLod lod, MeshData data)
    {
        var mesh = new Mesh(Context.GraphicsDevice, data);
        try
        {
            var entity = state.Chunk.Entity;
            if (World.Has<MeshRenderer>(entity))
            {
                ref var renderer = ref World.Get<MeshRenderer>(entity);
                renderer.Mesh = mesh;
            }
            else
            {
                World.Add(entity, new MeshRenderer(mesh, Color.White)
                {
                    UseBasicEffect = false
                });
            }

            var old = state.Mesh;
            state.Mesh = mesh;
            state.Lod = lod;
            old?.Dispose();
            if (old is null)
            {
                _loadedChunkCount++;
                if (_loadedChunkCount >= 16 &&
                    (_loadedChunkCount & (_loadedChunkCount - 1)) == 0)
                {
                    Console.WriteLine($"[terrain] {_loadedChunkCount}/{_states.Count} render chunks loaded " +
                        $"after {Stopwatch.GetElapsedTime(_streamStartedAt).TotalSeconds:F1}s; " +
                        "nearby LOD upgrades continue in the background.");
                }
            }
        }
        catch
        {
            mesh.Dispose();
            throw;
        }
    }

    private static float DistanceToChunk(in Vector3 position, in BoundingBox bounds)
    {
        var x = MathF.Max(bounds.Min.X - position.X, position.X - bounds.Max.X);
        var z = MathF.Max(bounds.Min.Z - position.Z, position.Z - bounds.Max.Z);
        return MathF.Sqrt(MathF.Max(0f, x) * MathF.Max(0f, x) +
            MathF.Max(0f, z) * MathF.Max(0f, z));
    }

    private enum RenderLod { Near, Middle, Far }

    private sealed class ChunkState(TerrainChunk chunk, BoundingBox bounds)
    {
        public TerrainChunk Chunk { get; } = chunk;
        public BoundingBox Bounds { get; } = bounds;
        public Mesh? Mesh { get; set; }
        public RenderLod Lod { get; set; } = RenderLod.Far;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame_project.Ecs;
using MonoGame_project.Engine;
using MonoGame_project.Engine.Diagnostics;
using MonoGame_project.Engine.Input;
using MonoGame_project.Engine.Physics;
using MonoGame_project.Engine.Physics.Components;
using MonoGame_project.Engine.Rendering.ThreeD;
using MonoGame_project.Engine.Scenes;
using MonoGame_project.Engine.Systems;
using MonoGame_project.Engine.Terrain;
using MonoGame_project.Game.Terrain;
using MonoGame_project.Game.UI;

namespace MonoGame_project.Game;

/// <summary>
/// Composition root with a temporary scene used to verify terrain, 3D rendering and physics.
/// </summary>
public sealed class BootstrapScene : GameScene
{
    private const float SphereRadius = 0.5f;
    private const int TerrainChunksPerAxis = 32;
    private const int TerrainFirstChunk = -TerrainChunksPerAxis / 2;
    private const int TerrainLastChunk = TerrainFirstChunk + TerrainChunksPerAxis - 1;
    private const int TerrainCellsPerChunk = 128;
    private const float TerrainWorldExtent = 5_000f;
    private const float TerrainChunkWorldSize = TerrainWorldExtent / TerrainChunksPerAxis;
    private const float TerrainCellSize = TerrainChunkWorldSize / TerrainCellsPerChunk;
    private const float TerrainHeightScale = 2_600f;
    private const float TerrainHeightOffset = 0f;
    private const float TerrainRenderMiddleMaxHeightError = 1f;
    private const float TerrainRenderFarMaxHeightError = 8f;
    private const int TerrainRenderFarMaxPatchCellSpan = 16;
    private const int TerrainCollisionChunkRadius = 1;

    private readonly List<TerrainChunk> _terrainChunks = [];
    private readonly TerrainRenderStreamingSystem _terrainRenderSystem;
    private Entity _cube = Entity.None;
    private Entity _sphere = Entity.None;
    private Entity _movingPlatform = Entity.None;
    private Mesh _cubeMesh = null!;
    private Mesh _sphereMesh = null!;
    private Mesh _movingPlatformMesh = null!;
    private Texture2D? _terrainDiffuse;
    private Vector2 _terrainWorldOrigin;
    private Vector2 _terrainWorldSize;
    private int _terrainImageWidth;
    private int _terrainImageHeight;

    public BootstrapScene()
    {
        _terrainRenderSystem = new TerrainRenderStreamingSystem(
            _terrainChunks, TerrainChunkWorldSize,
            TerrainRenderMiddleMaxHeightError,
            TerrainRenderFarMaxHeightError,
            TerrainRenderFarMaxPatchCellSpan,
            new Vector2(TerrainFirstChunk * TerrainChunkWorldSize),
            new Vector2(TerrainWorldExtent));
    }

    protected override void ConfigureSystems(SystemScheduler systems)
    {
        systems.Add(new PauseMenuSystem(), order: -2_000);
        systems.Add(new InspectCameraControlSystem(), order: -1_000);
        systems.Add(new TerrainColliderStreamingSystem(
            _terrainChunks,
            TerrainChunkWorldSize,
            TerrainCollisionChunkRadius), order: -500);
        systems.Add(_terrainRenderSystem, order: -400);
        systems.Add(new InspectSystem(), order: 1_050);
        systems.Add(new KinematicPlatformTestSystem(), order: -100);
        StartupProfiler.Info("Physics backend: Jolt.");
        systems.Add(new PhysicsSystem(), order: 0);
        systems.Add(new MeshRenderSystem(), order: 1_000);
        systems.Add(new PhysicsDebugRenderSystem(), order: 1_100);
    }

    protected override void OnInitialize(EngineContext context)
    {
        context.InputContexts.Reset(InputContexts.InspectViewport);
        World.SetResource(new GameUiTheme(context.GraphicsDevice));

        // One image covers the finite terrain. Render LODs and colliders use
        // the same CPU height chunks but triangulate them independently.
        TerrainHeightMap image;
        using (StartupProfiler.Measure("Height EXR read + decode"))
        {
            image = ExrTerrainAssets.LoadHeights(TerrainAssetPath("Rugged Terrain Height Map EXR.exr"));
        }

        StartupProfiler.Info(
            $"Terrain: {image.Width}x{image.Height} heightmap, " +
            $"{TerrainChunksPerAxis * TerrainChunksPerAxis} chunks, " +
            $"{TerrainCellsPerChunk} cells/chunk, " +
            $"{TerrainWorldExtent} world units across, " +
            $"height scale {TerrainHeightScale}, " +
            $"render LOD: near full, middle error {TerrainRenderMiddleMaxHeightError}, " +
            $"far error {TerrainRenderFarMaxHeightError}, " +
            $"far max patch {TerrainRenderFarMaxPatchCellSpan} cells, " +
            $"collision radius {TerrainCollisionChunkRadius}.");
        _terrainImageWidth = image.Width;
        _terrainImageHeight = image.Height;
        _terrainWorldOrigin = new Vector2(
            TerrainFirstChunk * TerrainChunkWorldSize);
        _terrainWorldSize = new Vector2(TerrainWorldExtent);
        var terrain = new HeightMapTerrainSource(
            image,
            _terrainWorldOrigin,
            _terrainWorldSize,
            TerrainHeightScale,
            TerrainHeightOffset);

        var camera = World.CreateEntity();
        World.Add(camera, new Transform3D(new Vector3(
            0f, terrain.SampleHeight(100f, 200f) + 120f, 0f)));
        World.Add(camera, new Camera3D(new Vector3(
            0f, terrain.SampleHeight(0f, -500f) + 20f, -500f))
        {
            NearPlane = 1f,
            FarPlane = 8_000f
        });
        World.Add(camera, new InspectCameraController3D(
            moveSpeed: 60f,
            mouseSensitivity: 0.0025f,
            pitchLimit: MathHelper.ToRadians(89f)));

        using (StartupProfiler.Measure("Terrain height fields + ECS entities"))
        {
            for (var z = TerrainFirstChunk; z <= TerrainLastChunk; z++)
            {
                for (var x = TerrainFirstChunk; x <= TerrainLastChunk; x++)
                {
                    var heights = new TerrainHeightField(
                        x, z, TerrainCellsPerChunk, TerrainCellSize, terrain.SampleHeight);
                    var chunk = World.CreateEntity();
                    World.Add(chunk, new Transform3D(new Vector3(heights.OriginX, 0f, heights.OriginZ)));
                    World.Add(chunk, new TerrainChunkTag());
                    _terrainChunks.Add(new TerrainChunk(chunk, heights));
                }
            }
        }

        _cube = World.CreateEntity();
        var objectGround = terrain.SampleHeight(4f, 4f);
        World.Add(_cube, new Transform3D(new Vector3(4f, objectGround + 4f, 4f)));
        World.Add(_cube, new PhysicsBody3D(
            PhysicsMotionType.Dynamic,
            mass: 1f,
            friction: 0.4f,
            restitution: 0f));
        World.Add(_cube, new BoxCollider3D(Vector3.One));

        _sphere = World.CreateEntity();
        World.Add(_sphere, new Transform3D(new Vector3(4f, objectGround + 6f, 4f)));
        World.Add(_sphere, new PhysicsBody3D(
            PhysicsMotionType.Dynamic,
            mass: 0.75f,
            friction: 0.4f,
            restitution: 0.15f));
        World.Add(_sphere, new SphereCollider3D(SphereRadius));

        var movingPlatformPosition = new Vector3(
            5f, terrain.SampleHeight(5f, 0f) + 2f, 0f);
        _movingPlatform = World.CreateEntity();
        World.Add(_movingPlatform, new Transform3D(movingPlatformPosition));
        World.Add(_movingPlatform, new PhysicsBody3D(
            PhysicsMotionType.Kinematic,
            affectedByGravity: false));
        World.Add(_movingPlatform, new BoxCollider3D(new Vector3(3f, 0.3f, 3f)));
        World.Add(
            _movingPlatform,
            new KinematicPlatformTestMotion(
                movingPlatformPosition,
                distance: 2f,
                angularSpeed: 0.8f));
    }

    protected override void OnLoadContent(EngineContext context)
    {
        try
        {
            using (StartupProfiler.Measure("Diffuse EXR + mipmaps + GPU texture"))
            {
                _terrainDiffuse = ExrTerrainAssets.LoadDiffuse(
                    context.GraphicsDevice,
                    TerrainAssetPath("Rugged Terrain Diffuse EXR.exr"),
                    _terrainImageWidth,
                    _terrainImageHeight);
            }

            _terrainRenderSystem.SetTexture(_terrainDiffuse);

            _cubeMesh = new Mesh(
                context.GraphicsDevice,
                ProceduralMeshFactory.CreateBox(Vector3.One));
            _sphereMesh = new Mesh(
                context.GraphicsDevice,
                ProceduralMeshFactory.CreateSphere(SphereRadius));
            _movingPlatformMesh = new Mesh(
                context.GraphicsDevice,
                ProceduralMeshFactory.CreateBox(new Vector3(3f, 0.3f, 3f)));

            World.Add(
                _cube,
                new MeshRenderer(_cubeMesh, new Color(224, 153, 74)));
            World.Add(
                _sphere,
                new MeshRenderer(_sphereMesh, new Color(190, 91, 126)));
            World.Add(
                _movingPlatform,
                new MeshRenderer(_movingPlatformMesh, new Color(78, 132, 196)));
        }
        catch
        {
            ReleaseContent();
            throw;
        }
    }

    protected override void OnUnloadContent() => ReleaseContent();

    private void ReleaseContent()
    {
        if (World.IsAlive(_cube))
        {
            World.Remove<MeshRenderer>(_cube);
        }

        if (World.IsAlive(_sphere))
        {
            World.Remove<MeshRenderer>(_sphere);
        }

        if (World.IsAlive(_movingPlatform))
        {
            World.Remove<MeshRenderer>(_movingPlatform);
        }

        _cubeMesh?.Dispose();
        _sphereMesh?.Dispose();
        _movingPlatformMesh?.Dispose();
        _terrainDiffuse?.Dispose();
        _terrainDiffuse = null;
        _cubeMesh = null!;
        _sphereMesh = null!;
        _movingPlatformMesh = null!;
    }

    private static string TerrainAssetPath(string filename) => Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "Terrains",
        "Rugged Terrain with Rivers",
        filename);
}

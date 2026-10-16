using Microsoft.Xna.Framework;
using MonoGame_project.Ecs;
using MonoGame_project.Engine;
using MonoGame_project.Engine.Input;
using MonoGame_project.Engine.Physics;
using MonoGame_project.Engine.Physics.Components;
using MonoGame_project.Engine.Rendering.ThreeD;
using MonoGame_project.Engine.Scenes;
using MonoGame_project.Engine.Systems;

namespace MonoGame_project.Game;

/// <summary>
/// Composition root with a temporary scene used to verify 3D rendering and physics.
/// </summary>
public sealed class BootstrapScene : GameScene
{
    private const float SphereRadius = 0.5f;

    private Entity _platform = Entity.None;
    private Entity _cube = Entity.None;
    private Entity _sphere = Entity.None;
    private Entity _movingPlatform = Entity.None;
    private Mesh _platformMesh = null!;
    private Mesh _cubeMesh = null!;
    private Mesh _sphereMesh = null!;
    private Mesh _movingPlatformMesh = null!;

    protected override void ConfigureSystems(SystemScheduler systems)
    {
        systems.Add(new InspectCameraControlSystem(), order: -1_000);
        systems.Add(new KinematicPlatformTestSystem(), order: -100);
        systems.Add(new PhysicsSystem(), order: 0);
        systems.Add(new MeshRenderSystem(), order: 1_000);
        systems.Add(new PhysicsDebugRenderSystem(), order: 1_100);
    }

    protected override void OnInitialize(EngineContext context)
    {
        context.InputContexts.Reset(InputContexts.InspectViewport);

        var camera = World.CreateEntity();
        World.Add(camera, new Transform3D(new Vector3(10f, 7f, 10f)));
        World.Add(camera, new Camera3D(new Vector3(0f, 1f, 0f)));
        World.Add(camera, new InspectCameraController3D());

        _platform = World.CreateEntity();
        World.Add(_platform, new Transform3D(new Vector3(0f, -0.25f, 0f)));
        World.Add(_platform, new PhysicsBody3D(
            PhysicsMotionType.Static,
            affectedByGravity: false));
        World.Add(_platform, new BoxCollider3D(new Vector3(10f, 0.5f, 10f)));

        _cube = World.CreateEntity();
        World.Add(_cube, new Transform3D(new Vector3(0f, 4f, 0f)));
        World.Add(_cube, new PhysicsBody3D(
            PhysicsMotionType.Dynamic,
            mass: 1f,
            friction: 0.4f,
            restitution: 0f));
        World.Add(_cube, new BoxCollider3D(Vector3.One));

        _sphere = World.CreateEntity();
        World.Add(_sphere, new Transform3D(new Vector3(0f, 6f, 0f)));
        World.Add(_sphere, new PhysicsBody3D(
            PhysicsMotionType.Dynamic,
            mass: 0.75f,
            friction: 0.4f,
            restitution: 0.15f));
        World.Add(_sphere, new SphereCollider3D(SphereRadius));

        var movingPlatformPosition = new Vector3(0f, 1.25f, 0f);
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
        var platformMeshData = ProceduralMeshFactory.CreateBox(new Vector3(10f, 0.5f, 10f));
        _platformMesh = new Mesh(context.GraphicsDevice, platformMeshData);
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
            _platform,
            new MeshRenderer(_platformMesh, new Color(92, 148, 82)));
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

    protected override void OnUnloadContent()
    {
        if (World.IsAlive(_platform))
        {
            World.Remove<MeshRenderer>(_platform);
        }

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

        _platformMesh.Dispose();
        _cubeMesh.Dispose();
        _sphereMesh.Dispose();
        _movingPlatformMesh.Dispose();
        _platformMesh = null!;
        _cubeMesh = null!;
        _sphereMesh = null!;
        _movingPlatformMesh = null!;
    }
}

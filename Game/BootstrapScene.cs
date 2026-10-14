using Microsoft.Xna.Framework;
using MonoGame_project.Ecs;
using MonoGame_project.Engine;
using MonoGame_project.Engine.Rendering.ThreeD;
using MonoGame_project.Engine.Scenes;
using MonoGame_project.Engine.Systems;

namespace MonoGame_project.Game;

/// <summary>
/// Composition root with a temporary camera and platform used to verify 3D rendering.
/// </summary>
public sealed class BootstrapScene : GameScene
{
    private Entity _platform = Entity.None;
    private Mesh _platformMesh = null!;

    protected override void ConfigureSystems(SystemScheduler systems)
    {
        systems.Add(new MeshRenderSystem(), order: 1_000);
    }

    protected override void OnInitialize(EngineContext context)
    {
        var camera = World.CreateEntity();
        World.Add(camera, new Transform3D(new Vector3(8f, 6f, 8f)));
        World.Add(camera, new Camera3D(Vector3.Zero));

        _platform = World.CreateEntity();
        World.Add(_platform, new Transform3D(new Vector3(0f, -0.25f, 0f)));
    }

    protected override void OnLoadContent(EngineContext context)
    {
        _platformMesh = ProceduralMeshFactory.CreateBox(
            context.GraphicsDevice,
            new Vector3(10f, 0.5f, 10f));

        World.Add(
            _platform,
            new MeshRenderer(_platformMesh, new Color(92, 148, 82)));
    }

    protected override void OnUnloadContent()
    {
        if (World.IsAlive(_platform))
        {
            World.Remove<MeshRenderer>(_platform);
        }

        _platformMesh.Dispose();
        _platformMesh = null!;
    }
}

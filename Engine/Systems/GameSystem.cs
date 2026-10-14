using MonoGame_project.Ecs;

namespace MonoGame_project.Engine.Systems;

/// <summary>Optional base class for systems that want retained world/context access.</summary>
public abstract class GameSystem : IGameSystem
{
    protected World World { get; private set; } = null!;
    protected EngineContext Context { get; private set; } = null!;

    void IGameSystem.Initialize(World world, EngineContext context)
    {
        World = world;
        Context = context;
        OnInitialize();
    }

    void IGameSystem.Shutdown(World world) => OnShutdown();

    protected virtual void OnInitialize() { }
    protected virtual void OnShutdown() { }
}

using System;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Diagnostics;
using MonoGame_project.Engine.Rendering;
using MonoGame_project.Engine.Systems;
using MonoGame_project.Engine.Time;

namespace MonoGame_project.Engine.Scenes;

/// <summary>
/// An isolated game state with its own ECS world and ordered systems.
/// </summary>
public abstract class GameScene : IDisposable
{
    private bool _configured;
    private bool _initialized;
    private bool _contentLoaded;
    private bool _disposed;

    protected GameScene()
    {
        World = new World();
        Systems = new SystemScheduler();
    }

    public World World { get; }
    public SystemScheduler Systems { get; }

    protected virtual void ConfigureSystems(SystemScheduler systems) { }
    protected virtual void OnInitialize(EngineContext context) { }
    protected virtual void OnLoadContent(EngineContext context) { }
    protected virtual void OnUnloadContent() { }
    protected virtual void OnShutdown() { }

    internal void Initialize(EngineContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
        {
            throw new InvalidOperationException("Scene is already initialized.");
        }

        if (!_configured)
        {
            ConfigureSystems(Systems);
            _configured = true;
        }

        OnInitialize(context);
        using (StartupProfiler.Measure("Scene systems Initialize"))
        {
            Systems.Initialize(World, context);
        }
        _initialized = true;
    }

    internal void LoadContent(EngineContext context)
    {
        EnsureInitialized();
        if (_contentLoaded)
        {
            return;
        }

        OnLoadContent(context);
        using (StartupProfiler.Measure("Scene systems LoadContent"))
        {
            Systems.LoadContent(context.Content);
        }
        _contentLoaded = true;
    }

    internal void Update(in FrameTime time) => Systems.Update(time);
    internal void FixedUpdate(in FixedTime time) => Systems.FixedUpdate(time);
    internal void Draw(in RenderContext context) => Systems.Draw(context);

    internal void UnloadContent()
    {
        if (!_contentLoaded)
        {
            return;
        }

        Systems.UnloadContent();
        OnUnloadContent();
        _contentLoaded = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_initialized)
        {
            UnloadContent();
            Systems.Shutdown();
            OnShutdown();
        }

        World.Dispose();
        _disposed = true;
    }

    private void EnsureInitialized()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized)
        {
            throw new InvalidOperationException("Scene has not been initialized.");
        }
    }
}

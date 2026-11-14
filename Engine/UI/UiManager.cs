using Gum;
using Gum.GueDeriving;
using Gum.Managers;
using Gum.Wireframe;
using Microsoft.Xna.Framework;
using System.Diagnostics;
using MonoGame_project.Engine.Diagnostics;

namespace MonoGame_project.Engine.UI;

/// <summary>Owns the Gum root and calls the mounted screens at each UI phase.</summary>
public sealed class UiManager : IDisposable
{
    private readonly GumService _gum;
    private readonly PerformanceBenchmarkRecorder? _benchmark;
    private ContainerRuntime _root = null!;
    private UiScope _global = null!;
    private UiScope _scene = null!;
    private bool _initialized;
    private bool _disposed;

    public UiManager(GumService gum, bool enabled = true,
        PerformanceBenchmarkRecorder? benchmark = null)
    {
        _gum = gum ?? throw new ArgumentNullException(nameof(gum));
        IsEnabled = enabled;
        _benchmark = benchmark;
    }

    public bool IsEnabled { get; }

    public UiScope Global => _initialized ? _global : throw new InvalidOperationException("UI is not initialized.");
    public UiScope Scene => _scene ?? throw new InvalidOperationException("No UI scene scope is active.");
    public bool SuspendsScene => (_scene?.SuspendsScene ?? false) ||
                                 (_global?.SuspendsScene ?? false);

    internal void Initialize()
    {
        if (!IsEnabled) return;
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
        {
            throw new InvalidOperationException("UI is already initialized.");
        }

        _root = new ContainerRuntime();
        _root.Dock(Dock.Fill);
        _root.AddToRoot();
        _global = new UiScope(_root, _gum.ModalRoot);
        _initialized = true;
    }

    internal void BeginScene()
    {
        if (!IsEnabled) return;
        EnsureInitialized();
        if (_scene is not null)
        {
            throw new InvalidOperationException("A UI scene scope is already active.");
        }

        _scene = new UiScope(_root, _gum.ModalRoot);
    }

    internal void EndScene()
    {
        if (!IsEnabled) return;
        _scene?.Dispose();
        _scene = null!;
    }

    internal void HandleInput()
    {
        if (!IsEnabled) return;
        var profiling = _benchmark?.IsCapturing == true;
        var started = profiling ? Stopwatch.GetTimestamp() : 0;
        EnsureInitialized();
        _global.HandleInput();
        _scene?.HandleInput();
        if (profiling) _benchmark!.RecordUiHandle(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    internal void Refresh(GameTime gameTime, bool hasActiveInput)
    {
        if (!IsEnabled) return;
        var profiling = _benchmark?.IsCapturing == true;
        var started = profiling ? Stopwatch.GetTimestamp() : 0;
        EnsureInitialized();
        _global.Refresh();
        _scene?.Refresh();
        if (profiling) _benchmark!.RecordUiRefresh(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        if (hasActiveInput)
        {
            started = profiling ? Stopwatch.GetTimestamp() : 0;
            _gum.Update(gameTime);
            if (profiling) _benchmark!.RecordGumUpdate(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    internal void Draw()
    {
        if (!IsEnabled) return;
        var profiling = _benchmark?.IsCapturing == true;
        var started = profiling ? Stopwatch.GetTimestamp() : 0;
        EnsureInitialized();
        _global.AfterSceneDraw();
        _scene?.AfterSceneDraw();
        if (profiling) _benchmark!.RecordUiAfterDraw(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        started = profiling ? Stopwatch.GetTimestamp() : 0;
        _gum.Draw();
        if (profiling) _benchmark!.RecordGumDraw(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    public void Dispose()
    {
        if (!IsEnabled)
        {
            _disposed = true;
            return;
        }
        if (_disposed)
        {
            return;
        }

        if (_initialized)
        {
            EndScene();
            _global.Dispose();
            _root.RemoveFromRoot();
            _initialized = false;
        }

        _disposed = true;
    }

    private void EnsureInitialized()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized)
        {
            throw new InvalidOperationException("UI is not initialized.");
        }
    }
}

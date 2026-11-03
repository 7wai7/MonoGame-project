using Gum;
using Gum.GueDeriving;
using Gum.Managers;
using Gum.Wireframe;
using Microsoft.Xna.Framework;

namespace MonoGame_project.Engine.UI;

/// <summary>Owns the Gum root and calls the mounted screens at each UI phase.</summary>
public sealed class UiManager : IDisposable
{
    private readonly GumService _gum;
    private ContainerRuntime _root = null!;
    private UiScope _global = null!;
    private UiScope _scene = null!;
    private bool _initialized;
    private bool _disposed;

    public UiManager(GumService gum) =>
        _gum = gum ?? throw new ArgumentNullException(nameof(gum));

    public UiScope Global => _initialized ? _global : throw new InvalidOperationException("UI is not initialized.");
    public UiScope Scene => _scene ?? throw new InvalidOperationException("No UI scene scope is active.");

    internal void Initialize()
    {
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
        EnsureInitialized();
        if (_scene is not null)
        {
            throw new InvalidOperationException("A UI scene scope is already active.");
        }

        _scene = new UiScope(_root, _gum.ModalRoot);
    }

    internal void EndScene()
    {
        _scene?.Dispose();
        _scene = null!;
    }

    internal void HandleInput()
    {
        EnsureInitialized();
        _global.HandleInput();
        _scene?.HandleInput();
    }

    internal void Refresh(GameTime gameTime, bool hasActiveInput)
    {
        EnsureInitialized();
        _global.Refresh();
        _scene?.Refresh();
        if (hasActiveInput)
        {
            _gum.Update(gameTime);
        }
    }

    internal void Draw()
    {
        EnsureInitialized();
        _global.AfterSceneDraw();
        _scene?.AfterSceneDraw();
        _gum.Draw();
    }

    public void Dispose()
    {
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

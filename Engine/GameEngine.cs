using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame_project.Engine.Rendering;
using MonoGame_project.Engine.Scenes;
using MonoGame_project.Engine.Time;

namespace MonoGame_project.Engine;

/// <summary>
/// Owns scene lifecycle and the variable-update/fixed-simulation/render loop.
/// MonoGame remains the platform host; this class is the game engine boundary.
/// </summary>
public sealed class GameEngine : IDisposable
{
    private readonly TimeSpan _fixedTimeStep;
    private readonly TimeSpan _maximumFrameTime;
    private GameScene _activeScene;
    private GameScene _pendingScene = null!;
    private EngineContext _context = null!;
    private TimeSpan _accumulator;
    private TimeSpan _frameTotal;
    private TimeSpan _fixedTotal;
    private FrameTime _lastFrameTime;
    private ulong _frameNumber;
    private ulong _tickNumber;
    private bool _initialized;
    private bool _contentLoaded;
    private bool _disposed;

    public GameEngine(
        GameScene initialScene,
        TimeSpan fixedTimeStep,
        TimeSpan maximumFrameTime)
    {
        ArgumentNullException.ThrowIfNull(initialScene);
        if (fixedTimeStep <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedTimeStep));
        }

        if (maximumFrameTime < fixedTimeStep)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFrameTime),
                "Maximum frame time must be at least one fixed step.");
        }

        _activeScene = initialScene;
        _fixedTimeStep = fixedTimeStep;
        _maximumFrameTime = maximumFrameTime;
    }

    public GameScene ActiveScene => _activeScene;
    public float InterpolationAlpha => (float)(_accumulator.TotalSeconds / _fixedTimeStep.TotalSeconds);

    public void Initialize(EngineContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        if (_initialized)
        {
            throw new InvalidOperationException("GameEngine is already initialized.");
        }

        _context = context;
        _activeScene.Initialize(context);
        _initialized = true;
    }

    public void LoadContent()
    {
        EnsureInitialized();
        if (_contentLoaded)
        {
            return;
        }

        _activeScene.LoadContent(_context!);
        _contentLoaded = true;
    }

    /// <summary>The transition is applied safely at the start of the next update.</summary>
    public void ChangeScene(GameScene scene)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        if (ReferenceEquals(scene, _activeScene) || ReferenceEquals(scene, _pendingScene))
        {
            return;
        }

        _pendingScene?.Dispose();
        _pendingScene = scene;
    }

    public void Update(GameTime gameTime)
    {
        EnsureInitialized();
        ApplyPendingScene();

        var elapsed = gameTime.ElapsedGameTime;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }
        else if (elapsed > _maximumFrameTime)
        {
            elapsed = _maximumFrameTime;
        }

        _frameTotal += elapsed;
        _accumulator += elapsed;
        _lastFrameTime = new FrameTime(elapsed, _frameTotal, _frameNumber++);

        _activeScene.Update(_lastFrameTime);

        while (_accumulator >= _fixedTimeStep)
        {
            _fixedTotal += _fixedTimeStep;
            var fixedTime = new FixedTime(_fixedTimeStep, _fixedTotal, _tickNumber++);
            _activeScene.FixedUpdate(fixedTime);
            _accumulator -= _fixedTimeStep;
        }
    }

    public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(spriteBatch);

        var renderContext = new RenderContext(
            _context!.GraphicsDevice,
            spriteBatch,
            gameTime,
            _lastFrameTime,
            InterpolationAlpha);

        _activeScene.Draw(renderContext);
    }

    public void UnloadContent()
    {
        if (!_contentLoaded)
        {
            return;
        }

        _activeScene.UnloadContent();
        _contentLoaded = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        UnloadContent();
        _pendingScene?.Dispose();
        _pendingScene = null!;
        _activeScene.Dispose();
        _context = null!;
        _initialized = false;
        _disposed = true;
    }

    private void ApplyPendingScene()
    {
        if (_pendingScene is null)
        {
            return;
        }

        var nextScene = _pendingScene;
        _pendingScene = null!;

        _activeScene.Dispose();
        _activeScene = nextScene;
        _activeScene.Initialize(_context!);
        if (_contentLoaded)
        {
            _activeScene.LoadContent(_context!);
        }

        _accumulator = TimeSpan.Zero;
    }

    private void EnsureInitialized()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized)
        {
            throw new InvalidOperationException("GameEngine has not been initialized.");
        }
    }
}

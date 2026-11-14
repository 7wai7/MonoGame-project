using Gum;
using System.Diagnostics;
using Gum.Wireframe;
using Gum.GueDeriving;
using KernSmith.Gum;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame_project.Configuration;
using MonoGame_project.Engine;
using MonoGame_project.Engine.Diagnostics;
using MonoGame_project.Engine.Input;
using MonoGame_project.Engine.UI;
using MonoGame_project.Game;
using MonoGame_project.Game.UI;

namespace MonoGame_project;

public sealed class MainGame : Microsoft.Xna.Framework.Game
{
    private readonly GameSettings _settings = GameSettings.Default;
    private readonly PerformanceBenchmarkOptions? _benchmarkOptions;
    private readonly PerformanceBenchmarkRecorder? _benchmarkRecorder;
    private readonly UserSettingsStore _userSettings = new();
    private readonly float _uiScaleAtStartup;
    private readonly GraphicsDeviceManager _graphics;
    private readonly InputService _input;
    private readonly InputContextStack _inputContexts;
    private readonly ApplicationControl _application = new();
    private readonly GameEngine _engine;
    private SpriteBatch _spriteBatch = null!;
    private RenderTarget2D? _benchmarkTarget;
    private bool _firstUpdateMeasured;
    private bool _firstFrameDrawn;
    private float _appliedUiZoom;

    private static GumService GumUI => GumService.Default;

    public MainGame(PerformanceBenchmarkOptions? benchmarkOptions = null)
    {
        _benchmarkOptions = benchmarkOptions;
        _benchmarkRecorder = benchmarkOptions is null ? null : new PerformanceBenchmarkRecorder(benchmarkOptions);
        _uiScaleAtStartup = _userSettings.Current.UiScale;
        _graphics = new GraphicsDeviceManager(this);
        _graphics.GraphicsProfile = GraphicsProfile.HiDef;
        _input = new InputService(this);
        _inputContexts = new InputContextStack(_input);
        _graphics.PreferredBackBufferWidth = _settings.BackBufferWidth;
        _graphics.PreferredBackBufferHeight = _settings.BackBufferHeight;
        _graphics.SynchronizeWithVerticalRetrace = benchmarkOptions is null && _userSettings.Current.VSync;
        _graphics.HardwareModeSwitch = false;
        _graphics.IsFullScreen = benchmarkOptions?.Fullscreen ?? _userSettings.Current.Fullscreen;
        _graphics.PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8;
        _graphics.PreferMultiSampling = true;

        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        IsFixedTimeStep = false;
        Window.Title = _settings.WindowTitle;

        _engine = new GameEngine(
            new BootstrapScene(benchmarkOptions),
            _settings.FixedTimeStep,
            _settings.MaximumFrameTime);
    }

    protected override void Initialize()
    {
        using (StartupProfiler.Measure("UI + engine Initialize (scene + systems)"))
        {
            LogBenchmarkDisplay("before Gum initialization");
            if (_benchmarkOptions?.UiEnabled != false)
            {
                GumUI.Initialize(this, "GumProject/GameUi.gumx");
                GumUI.EnableExpandToWindow(_uiScaleAtStartup);
                _appliedUiZoom = _uiScaleAtStartup;
                var fonts = new KernSmithFontCreator(GraphicsDevice);
                KernSmithFontCreator.RegisterFont("Poppins", "Fonts/Poppins-Medium.ttf");
                CustomSetPropertyOnRenderable.InMemoryFontCreator = fonts;
                // Prepare fonts once when UI elements are created, outside Gum.Draw.
                TextRuntime.UseFontOversampling = false;
                UiText.RasterScale = MathF.Max(1f,
                    _uiScaleAtStartup * GraphicsDevice.PresentationParameters.BackBufferWidth /
                    Math.Max(1, Window.ClientBounds.Width));
            }
            LogBenchmarkDisplay("after Gum initialization");

            var context = new EngineContext(
                GraphicsDevice,
                Content,
                Window,
                Services,
                _input,
                _inputContexts,
                _application,
                new UiManager(GumUI, _benchmarkOptions?.UiEnabled != false, _benchmarkRecorder),
                _userSettings,
                _benchmarkRecorder);

            _engine.Initialize(context);
        }

        // MonoGame may enter LoadContent from its base initialization path,
        // so the engine must be ready before this call.
        using (StartupProfiler.Measure("MonoGame base.Initialize (includes LoadContent)"))
        {
            base.Initialize();
        }
        LogBenchmarkDisplay("after MonoGame initialization");
        _benchmarkRecorder?.WriteEnvironment(GraphicsDevice, Window, _uiScaleAtStartup);
    }

    protected override void LoadContent()
    {
        using var startupStage = StartupProfiler.Measure("MonoGame LoadContent (scene + systems)");
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        if (_benchmarkOptions?.UsesFixedRenderTarget == true)
        {
            _benchmarkTarget = new RenderTarget2D(GraphicsDevice,
                _benchmarkOptions.InternalWidth, _benchmarkOptions.InternalHeight,
                false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
        }
        _engine.LoadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        if (!_firstUpdateMeasured)
        {
            using (StartupProfiler.Measure("First Update"))
            {
                UpdateFrame(gameTime);
            }

            _firstUpdateMeasured = true;
            return;
        }

        UpdateFrame(gameTime);
    }

    private void UpdateFrame(GameTime gameTime)
    {
        _benchmarkRecorder?.BeginFrame();
        var profiling = _benchmarkRecorder?.IsCapturing == true;
        var started = profiling ? Stopwatch.GetTimestamp() : 0;
        UpdateUiZoomForDisplayDensity();
        _engine.Update(gameTime);
        if (profiling)
            _benchmarkRecorder!.RecordUpdate(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        // A settings screen needs only UI frames. Cap them even when VSync is off.
        if (IsFixedTimeStep != _engine.IsSceneSuspended)
        {
            IsFixedTimeStep = _engine.IsSceneSuspended;
            if (IsFixedTimeStep)
            {
                TargetElapsedTime = TimeSpan.FromSeconds(1d / 30d);
            }
        }
        ApplyDisplayPreferences();

        if (_application.ExitRequested)
        {
            Exit();
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (!_firstFrameDrawn)
        {
            using (StartupProfiler.Measure("First Draw"))
            {
                DrawFrame(gameTime);
            }

            StartupProfiler.Complete();
            _firstFrameDrawn = true;
            return;
        }

        DrawFrame(gameTime);
    }

    private void DrawFrame(GameTime gameTime)
    {
        var profiling = _benchmarkRecorder?.IsCapturing == true;
        var started = profiling ? Stopwatch.GetTimestamp() : 0;
        if (_benchmarkTarget is not null) GraphicsDevice.SetRenderTarget(_benchmarkTarget);
        GraphicsDevice.Clear(_settings.ClearColor);

        _engine.Draw(gameTime, _spriteBatch!);
        base.Draw(gameTime);
        if (_benchmarkTarget is not null)
        {
            GraphicsDevice.SetRenderTarget(null);
            var backBuffer = GraphicsDevice.PresentationParameters;
            _spriteBatch.Begin(SpriteSortMode.Immediate,
                Microsoft.Xna.Framework.Graphics.BlendState.Opaque,
                SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone);
            _spriteBatch.Draw(_benchmarkTarget,
                new Rectangle(0, 0, backBuffer.BackBufferWidth, backBuffer.BackBufferHeight),
                Color.White);
            _spriteBatch.End();
        }
        if (profiling)
            _benchmarkRecorder!.EndFrame(started,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds, GraphicsDevice, Window);
    }

    private void UpdateUiZoomForDisplayDensity()
    {
        if (_benchmarkOptions?.UiEnabled == false) return;
        var windowWidth = Window.ClientBounds.Width;
        if (windowWidth <= 0) return;

        var backBufferWidth = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var uiZoom = _uiScaleAtStartup * backBufferWidth / windowWidth;
        if (!float.IsFinite(uiZoom) || uiZoom <= 0f ||
            MathF.Abs(uiZoom - _appliedUiZoom) < 0.001f) return;

        GumUI.EnableExpandToWindow(uiZoom);
        _appliedUiZoom = uiZoom;
    }

    private void ApplyDisplayPreferences()
    {
        if (_benchmarkOptions is not null) return;
        var preferred = _userSettings.Current;
        if (_graphics.IsFullScreen == preferred.Fullscreen &&
            _graphics.SynchronizeWithVerticalRetrace == preferred.VSync)
        {
            return;
        }

        _graphics.IsFullScreen = preferred.Fullscreen;
        _graphics.SynchronizeWithVerticalRetrace = preferred.VSync;
        _graphics.ApplyChanges();
    }

    private void LogBenchmarkDisplay(string phase)
    {
        if (_benchmarkRecorder is null) return;
        var buffer = GraphicsDevice.PresentationParameters;
        _benchmarkRecorder.Event("display", $"{phase}: window {Window.ClientBounds.Width}x{Window.ClientBounds.Height}, " +
            $"backbuffer {buffer.BackBufferWidth}x{buffer.BackBufferHeight}, " +
            $"preferred {_graphics.PreferredBackBufferWidth}x{_graphics.PreferredBackBufferHeight}, " +
            $"fullscreen {_graphics.IsFullScreen}, " +
            $"adapter mode {GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Width}x" +
            $"{GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Height}.");
    }

    protected override void UnloadContent()
    {
        _engine.UnloadContent();
        _benchmarkTarget?.Dispose();
        _benchmarkTarget = null;
        _spriteBatch.Dispose();
        _spriteBatch = null!;

        base.UnloadContent();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _engine.Dispose();
            _input.Dispose();
            if (_benchmarkOptions?.UiEnabled != false) GumUI.Uninitialize();
            _benchmarkRecorder?.Dispose();
        }

        base.Dispose(disposing);
    }
}

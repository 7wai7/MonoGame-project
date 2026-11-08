using Gum;
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
    private readonly UserSettingsStore _userSettings = new();
    private readonly float _uiScaleAtStartup;
    private readonly GraphicsDeviceManager _graphics;
    private readonly InputService _input;
    private readonly InputContextStack _inputContexts;
    private readonly ApplicationControl _application = new();
    private readonly GameEngine _engine;
    private SpriteBatch _spriteBatch = null!;
    private bool _firstUpdateMeasured;
    private bool _firstFrameDrawn;
    private float _appliedUiZoom;

    private static GumService GumUI => GumService.Default;

    public MainGame()
    {
        _uiScaleAtStartup = _userSettings.Current.UiScale;
        _graphics = new GraphicsDeviceManager(this);
        _graphics.GraphicsProfile = GraphicsProfile.HiDef;
        _input = new InputService(this);
        _inputContexts = new InputContextStack(_input);
        _graphics.PreferredBackBufferWidth = _settings.BackBufferWidth;
        _graphics.PreferredBackBufferHeight = _settings.BackBufferHeight;
        _graphics.SynchronizeWithVerticalRetrace = _userSettings.Current.VSync;
        _graphics.HardwareModeSwitch = false;
        _graphics.IsFullScreen = _userSettings.Current.Fullscreen;
        _graphics.PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8;
        _graphics.PreferMultiSampling = true;

        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        IsFixedTimeStep = false;
        Window.Title = _settings.WindowTitle;

        _engine = new GameEngine(
            new BootstrapScene(),
            _settings.FixedTimeStep,
            _settings.MaximumFrameTime);
    }

    protected override void Initialize()
    {
        using (StartupProfiler.Measure("Gum + engine Initialize (scene + systems)"))
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

            var context = new EngineContext(
                GraphicsDevice,
                Content,
                Window,
                Services,
                _input,
                _inputContexts,
                _application,
                new UiManager(GumUI),
                _userSettings);

            _engine.Initialize(context);
        }

        // MonoGame may enter LoadContent from its base initialization path,
        // so the engine must be ready before this call.
        using (StartupProfiler.Measure("MonoGame base.Initialize (includes LoadContent)"))
        {
            base.Initialize();
        }
    }

    protected override void LoadContent()
    {
        using var startupStage = StartupProfiler.Measure("MonoGame LoadContent (scene + systems)");
        _spriteBatch = new SpriteBatch(GraphicsDevice);
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
        UpdateUiZoomForDisplayDensity();
        _engine.Update(gameTime);
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
        GraphicsDevice.Clear(_settings.ClearColor);

        _engine.Draw(gameTime, _spriteBatch!);
        base.Draw(gameTime);
    }

    private void UpdateUiZoomForDisplayDensity()
    {
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

    protected override void UnloadContent()
    {
        _engine.UnloadContent();
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
            GumUI.Uninitialize();
        }

        base.Dispose(disposing);
    }
}

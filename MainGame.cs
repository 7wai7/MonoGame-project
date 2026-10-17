using Gum;
using Gum.Forms;
using Gum.Wireframe;
using KernSmith.Gum;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame_project.Configuration;
using MonoGame_project.Engine;
using MonoGame_project.Engine.Input;
using MonoGame_project.Game;

namespace MonoGame_project;

public sealed class MainGame : Microsoft.Xna.Framework.Game
{
    private readonly GameSettings _settings = GameSettings.Default;
    private readonly GraphicsDeviceManager _graphics;
    private readonly InputService _input;
    private readonly InputContextStack _inputContexts;
    private readonly ApplicationControl _application = new();
    private readonly GameEngine _engine;
    private SpriteBatch _spriteBatch = null!;

    private static GumService GumUI => GumService.Default;

    public MainGame()
    {
        _graphics = new GraphicsDeviceManager(this);
        _input = new InputService(this);
        _inputContexts = new InputContextStack(_input);
        _graphics.PreferredBackBufferWidth = _settings.BackBufferWidth;
        _graphics.PreferredBackBufferHeight = _settings.BackBufferHeight;
        _graphics.SynchronizeWithVerticalRetrace = _settings.VSync;
        _graphics.PreferredDepthStencilFormat = DepthFormat.Depth24;
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
        GumUI.Initialize(this, DefaultVisualsVersion.V3);
        CustomSetPropertyOnRenderable.InMemoryFontCreator =
            new KernSmithFontCreator(GraphicsDevice);

        var context = new EngineContext(
            GraphicsDevice,
            Content,
            Window,
            Services,
            _input,
            _inputContexts,
            _application);

        _engine.Initialize(context);

        // MonoGame may enter LoadContent from its base initialization path,
        // so the engine must be ready before this call.
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _engine.LoadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        _engine.Update(gameTime);
        if (_input.HasActiveInput)
        {
            GumUI.Update(gameTime);
        }

        if (_application.ExitRequested)
        {
            Exit();
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(_settings.ClearColor);

        _engine.Draw(gameTime, _spriteBatch!);
        GumUI.Draw();
        base.Draw(gameTime);
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

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoGame_project.Configuration;
using MonoGame_project.Engine.Input;
using MonoGame_project.Engine.Diagnostics;
using MonoGame_project.Engine.UI;

namespace MonoGame_project.Engine;

/// <summary>
/// Long-lived MonoGame services available to scenes and systems.
/// </summary>
public sealed class EngineContext
{
    public EngineContext(
        GraphicsDevice graphicsDevice,
        ContentManager content,
        GameWindow window,
        GameServiceContainer services,
        InputService input,
        InputContextStack inputContexts,
        ApplicationControl application,
        UiManager ui,
        UserSettingsStore settings,
        PerformanceBenchmarkRecorder? benchmark = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(inputContexts);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(settings);
        GraphicsDevice = graphicsDevice;
        Content = content;
        Window = window;
        Services = services;
        Input = input;
        InputContexts = inputContexts;
        Application = application;
        Ui = ui;
        Settings = settings;
        Benchmark = benchmark;
    }

    public GraphicsDevice GraphicsDevice { get; }
    public ContentManager Content { get; }
    public GameWindow Window { get; }
    public GameServiceContainer Services { get; }
    public InputService Input { get; }
    public InputContextStack InputContexts { get; }
    public ApplicationControl Application { get; }
    public UiManager Ui { get; }
    public UserSettingsStore Settings { get; }
    public PerformanceBenchmarkRecorder? Benchmark { get; }
}

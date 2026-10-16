using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoGame_project.Engine.Input;

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
        InputContextStack inputContexts)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(inputContexts);
        GraphicsDevice = graphicsDevice;
        Content = content;
        Window = window;
        Services = services;
        Input = input;
        InputContexts = inputContexts;
    }

    public GraphicsDevice GraphicsDevice { get; }
    public ContentManager Content { get; }
    public GameWindow Window { get; }
    public GameServiceContainer Services { get; }
    public InputService Input { get; }
    public InputContextStack InputContexts { get; }
}

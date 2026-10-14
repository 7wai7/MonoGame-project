using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

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
        GameServiceContainer services)
    {
        GraphicsDevice = graphicsDevice;
        Content = content;
        Window = window;
        Services = services;
    }

    public GraphicsDevice GraphicsDevice { get; }
    public ContentManager Content { get; }
    public GameWindow Window { get; }
    public GameServiceContainer Services { get; }
}

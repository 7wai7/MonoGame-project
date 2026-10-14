using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame_project.Engine.Time;

namespace MonoGame_project.Engine.Rendering;

/// <summary>Rendering dependencies and interpolation state for the current frame.</summary>
public readonly record struct RenderContext(
    GraphicsDevice GraphicsDevice,
    SpriteBatch SpriteBatch,
    GameTime GameTime,
    FrameTime FrameTime,
    float InterpolationAlpha);

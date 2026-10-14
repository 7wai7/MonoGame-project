using Microsoft.Xna.Framework.Content;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Rendering;
using MonoGame_project.Engine.Time;

namespace MonoGame_project.Engine.Systems;

public interface IGameSystem
{
    void Initialize(World world, EngineContext context) { }
    void Shutdown(World world) { }
}

public interface IContentSystem : IGameSystem
{
    void LoadContent(World world, ContentManager content);
    void UnloadContent(World world);
}

public interface IUpdateSystem : IGameSystem
{
    void Update(World world, in FrameTime time);
}

public interface IFixedUpdateSystem : IGameSystem
{
    void FixedUpdate(World world, in FixedTime time);
}

public interface IRenderSystem : IGameSystem
{
    void Draw(World world, in RenderContext context);
}

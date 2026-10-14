using MonoGame_project.Engine.Scenes;
using MonoGame_project.Engine.Systems;

namespace MonoGame_project.Game;

/// <summary>
/// Empty composition root for future game-specific systems and initial entities.
/// </summary>
public sealed class BootstrapScene : GameScene
{
    protected override void ConfigureSystems(SystemScheduler systems)
    {
        // Register future systems here in execution order, for example:
        // systems.Add(new InputSystem(), order: 0);
        // systems.Add(new MovementSystem(), order: 100);
        // systems.Add(new SpriteRenderSystem(), order: 1000);
    }
}

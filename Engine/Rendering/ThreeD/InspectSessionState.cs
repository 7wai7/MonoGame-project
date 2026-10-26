using MonoGame_project.Ecs;

namespace MonoGame_project.Engine.Rendering.ThreeD;

/// <summary>Selection and orbit target shared by inspect UI and camera control.</summary>
internal sealed class InspectSessionState
{
    public Entity Selected { get; set; } = Entity.None;
    public Entity OrbitTarget { get; set; } = Entity.None;
}

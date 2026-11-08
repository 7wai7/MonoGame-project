using Gum.Wireframe;

namespace MonoGame_project.Engine.UI;

/// <summary>A UI subtree with its own local behavior and lifetime.</summary>
public abstract class UiScreen : IDisposable
{
    public abstract GraphicalUiElement Visual { get; }

    /// <summary>Visible modal screens may suspend scene simulation and 3D drawing.</summary>
    public virtual bool SuspendsScene => false;

    // UI input runs before ECS updates so a menu can block camera input immediately.
    public virtual void HandleInput() { }

    // Read the latest world state after ECS and physics have finished this frame.
    public virtual void Refresh() { }

    // Diagnostics can sample the completed 3D render before Gum is drawn.
    public virtual void AfterSceneDraw() { }

    public virtual void Dispose() { }
}

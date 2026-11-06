using Gum.GueDeriving;
using Gum.Wireframe;

namespace MonoGame_project.Game.UI;

/// <summary>The inspect-mode center mark, independent of object selection.</summary>
internal sealed class InspectReticleView
{
    private readonly CircleRuntime _circle;

    public InspectReticleView(GameUiTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _circle = new CircleRuntime
        {
            Width = 8f,
            Height = 8f,
            IsFilled = true,
            FillColor = theme.TextPrimary,
            StrokeWidth = 0f
        };
        _circle.Anchor(Anchor.Center);
    }

    public GraphicalUiElement Visual => _circle;

    public bool Visible
    {
        get => _circle.Visible;
        set => _circle.Visible = value;
    }
}

using Gum.DataTypes;
using Gum.GueDeriving;
using Gum.Managers;
using Gum.Wireframe;

namespace MonoGame_project.Game.UI;

/// <summary>Three-line overview shown above the component list.</summary>
internal sealed class InspectSummaryView
{
    private readonly ContainerRuntime _root = new()
    {
        Width = 0f,
        WidthUnits = DimensionUnitType.RelativeToParent,
        Height = 94f,
        ChildrenLayout = ChildrenLayout.TopToBottomStack,
        StackSpacing = 4f
    };
    private readonly TextRuntime _primary;
    private readonly TextRuntime _secondary;
    private readonly TextRuntime _state;

    public InspectSummaryView(GameUiTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _primary = UiText.Create(15f, theme.TextSecondary, 22f);
        _secondary = UiText.Create(15f, theme.TextSecondary, 22f);
        _state = UiText.Create(15f, theme.TextSecondary, 42f);
        _root.Children.Add(_primary);
        _root.Children.Add(_secondary);
        _root.Children.Add(_state);
    }

    public GraphicalUiElement Visual => _root;

    public bool Visible
    {
        get => _root.Visible;
        set => _root.Visible = value;
    }

    public void Set(InspectSummaryData data)
    {
        UiText.Set(_primary, data.Primary);
        UiText.Set(_secondary, data.Secondary);
        UiText.Set(_state, data.State);
    }
}

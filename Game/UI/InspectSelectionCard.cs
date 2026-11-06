using Gum.DataTypes;
using Gum.Forms.Controls;
using Gum.GueDeriving;
using Gum.Managers;
using Gum.Wireframe;

namespace MonoGame_project.Game.UI;

/// <summary>Compact selected-object card; asks its owner to open details.</summary>
internal sealed class InspectSelectionCard : IDisposable
{
    private const float MaxWidth = 306f;

    private readonly ContainerRuntime _panel;
    private readonly TextRuntime _title;
    private readonly Button _details;
    private readonly Action _openDetails;

    public InspectSelectionCard(GameUiTheme theme, Action openDetails)
    {
        _openDetails = openDetails ?? throw new ArgumentNullException(nameof(openDetails));
        _panel = theme.CreatePanel();
        _panel.Anchor(Anchor.TopRight);
        _panel.Width = 0f;
        _panel.WidthUnits = DimensionUnitType.RelativeToParent;
        _panel.MinWidth = 1f;
        _panel.MaxWidth = MaxWidth;
        _panel.Height = 68f;
        _panel.Visible = false;

        var content = new ContainerRuntime
        {
            X = 16f,
            Y = 15f,
            Width = -32f,
            WidthUnits = DimensionUnitType.RelativeToParent,
            Height = 38f,
            ChildrenLayout = ChildrenLayout.LeftToRightStack,
            StackSpacing = 8f
        };
        _panel.Children.Add(content);
        _title = UiText.Create(15f, GameUiTheme.TextPrimary, 38f);
        _title.Width = 1f;
        _title.WidthUnits = DimensionUnitType.Ratio;
        content.Children.Add(_title);
        _details = theme.CreateButton("Details");
        _details.Width = 86f;
        _details.Height = 38f;
        _details.Click += OnDetailsClicked;
        content.AddChild(_details);
    }

    public GraphicalUiElement Visual => _panel;

    public bool Visible
    {
        get => _panel.Visible;
        set => _panel.Visible = value;
    }

    public void Show(string label, bool allowDetails)
    {
        UiText.Set(_title, label);
        _details.Visual.Visible = allowDetails;
        _panel.Visible = true;
    }

    public void Dispose() => _details.Click -= OnDetailsClicked;

    private void OnDetailsClicked(object? sender, EventArgs args) => _openDetails();
}

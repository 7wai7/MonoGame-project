using Gum.DataTypes;
using Gum.Forms.Controls;
using Gum.GueDeriving;
using Gum.Managers;
using Gum.Wireframe;

namespace MonoGame_project.Game.UI;

/// <summary>Opt-in inspector panel composed from summary and component-list views.</summary>
internal sealed class InspectDetailsPanel : IDisposable
{
    private const float MaxWidth = 370f;
    private const float MaxHeight = 620f;

    private readonly ContainerRuntime _panel;
    private readonly TextRuntime _title;
    private readonly TextRuntime _listHeading;
    private readonly TextRuntime _footer;
    private readonly Button _closeButton;
    private readonly InspectSummaryView _summary = new();
    private readonly InspectComponentList _componentList;
    private readonly Action _close;

    public InspectDetailsPanel(GameUiTheme theme, Action close, Action<int> rowClicked)
    {
        _close = close ?? throw new ArgumentNullException(nameof(close));
        _componentList = new InspectComponentList(rowClicked);
        _panel = theme.CreatePanel(MaxWidth, MaxHeight);
        _panel.Anchor(Anchor.TopRight);
        _panel.Width = 0f;
        _panel.WidthUnits = DimensionUnitType.RelativeToParent;
        _panel.MinWidth = 1f;
        _panel.MaxWidth = MaxWidth;
        _panel.Height = 0f;
        _panel.HeightUnits = DimensionUnitType.RelativeToParent;
        _panel.MinHeight = 1f;
        _panel.MaxHeight = MaxHeight;
        _panel.Visible = false;

        var content = new ContainerRuntime
        {
            X = 18f,
            Y = 14f,
            Width = -36f,
            WidthUnits = DimensionUnitType.RelativeToParent,
            Height = -28f,
            HeightUnits = DimensionUnitType.RelativeToParent,
            ChildrenLayout = ChildrenLayout.TopToBottomStack,
            StackSpacing = 8f
        };
        _panel.Children.Add(content);

        var header = new ContainerRuntime
        {
            Width = 0f,
            WidthUnits = DimensionUnitType.RelativeToParent,
            Height = 36f,
            ChildrenLayout = ChildrenLayout.LeftToRightStack,
            StackSpacing = 8f
        };
        content.Children.Add(header);
        _title = UiText.Create(19f, GameUiTheme.TextPrimary, 36f);
        _title.Width = 1f;
        _title.WidthUnits = DimensionUnitType.Ratio;
        header.Children.Add(_title);
        _closeButton = theme.CreateButton("Close", 72f, 36f);
        _closeButton.Click += OnCloseClicked;
        header.AddChild(_closeButton);

        content.Children.Add(_summary.Visual);
        _listHeading = UiText.Create(14f, GameUiTheme.Accent, 20f);
        _listHeading.Text = "Details";
        content.Children.Add(_listHeading);
        content.Children.Add(_componentList.Visual);
        _footer = UiText.Create(12f, GameUiTheme.TextSecondary, 20f);
        _footer.Text = "Ctrl + wheel  ·  I close";
        content.Children.Add(_footer);
    }

    public GraphicalUiElement Visual => _panel;

    public bool Visible
    {
        get => _panel.Visible;
        set => _panel.Visible = value;
    }

    public void RefreshResponsiveVisibility()
    {
        var availableHeight = _panel.AbsoluteHeight;
        _summary.Visible = availableHeight >= 300f;
        _listHeading.Visible = availableHeight >= 160f;
        _footer.Visible = availableHeight >= 190f;
    }

    public void SetSummary(InspectSummaryData data)
    {
        UiText.Set(_title, data.Title);
        _summary.Set(data);
    }

    public void SetRows(int revision, IReadOnlyList<InspectRowDisplay> rows) =>
        _componentList.SetRows(revision, rows);

    public void ResetRows() => _componentList.Reset();

    public void Dispose()
    {
        _closeButton.Click -= OnCloseClicked;
        _componentList.Dispose();
    }

    private void OnCloseClicked(object? sender, EventArgs args) => _close();
}

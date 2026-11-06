using Gum.DataTypes;
using Gum.Forms.Controls;
using Gum.Forms.DefaultVisuals.V3;
using Gum.Wireframe;

namespace MonoGame_project.Game.UI;

/// <summary>Scrollable component rows; Gum owns clipping, wheel input and scrollbar.</summary>
internal sealed class InspectComponentList : IDisposable
{
    private const float RowHeight = 28f;

    private readonly ScrollViewer _viewer = new();
    private readonly List<UiTextRow> _rows = [];
    private readonly GameUiTheme _theme;
    private readonly Action<int> _rowClicked;
    private int _revision = -1;

    public InspectComponentList(GameUiTheme theme, Action<int> rowClicked)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _rowClicked = rowClicked ?? throw new ArgumentNullException(nameof(rowClicked));
        _viewer.Visual.Width = 0f;
        _viewer.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        _viewer.Visual.Height = 1f;
        _viewer.Visual.HeightUnits = DimensionUnitType.Ratio;
        _viewer.MouseWheelScrollSpeed = RowHeight * 3f;
        _viewer.InnerPanel.StackSpacing = 0f;
        var visual = (ScrollViewerVisual)_viewer.Visual;
        visual.Background.Visible = false;
        visual.FocusedIndicator.Visible = false;
    }

    public GraphicalUiElement Visual => _viewer.Visual;

    public void Reset()
    {
        _viewer.VerticalScrollBarValue = 0f;
        _revision = -1;
    }

    public void SetRows(int revision, IReadOnlyList<InspectRowDisplay> rows)
    {
        if (_revision != revision)
        {
            Rebuild(rows.Count);
            _revision = revision;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            _rows[index].Set(row.Text,
                row.Secondary ? _theme.TextSecondary : _theme.TextPrimary, row.Clickable);
        }
    }

    public void Dispose()
    {
        foreach (var row in _rows)
        {
            _viewer.RemoveChild(row.Visual);
            row.Dispose();
        }

        _rows.Clear();
    }

    private void Rebuild(int rowCount)
    {
        var previousScroll = _viewer.VerticalScrollBarValue;
        foreach (var row in _rows)
        {
            _viewer.RemoveChild(row.Visual);
            row.Dispose();
        }

        _rows.Clear();
        for (var index = 0; index < rowCount; index++)
        {
            var rowIndex = index;
            var row = new UiTextRow(_theme, RowHeight, () => _rowClicked(rowIndex));
            _viewer.AddChild(row.Visual);
            _rows.Add(row);
        }

        _viewer.VerticalScrollBarValue = Math.Min(previousScroll,
            _viewer.VerticalScrollBarMaximum);
    }
}

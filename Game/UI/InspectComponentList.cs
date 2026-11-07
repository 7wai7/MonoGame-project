using Gum.DataTypes;
using Gum.Forms.Controls;
using Gum.Forms.DefaultVisuals.V3;
using Gum.Wireframe;
using Microsoft.Xna.Framework;

namespace MonoGame_project.Game.UI;

/// <summary>Scrollable component rows; Gum owns clipping, wheel input and scrollbar.</summary>
internal sealed class InspectComponentList : IDisposable
{
    private const float RowHeight = 32f;

    private readonly ScrollViewer _viewer = new();
    private readonly List<UiTextRow> _rows = [];
    private readonly Action<int> _rowClicked;
    private int _revision = -1;

    public InspectComponentList(Action<int> rowClicked)
    {
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
        StyleScrollBar(visual);
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
                row.Secondary ? HudPalette.Muted : HudPalette.Text, row.Clickable);
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
            var row = new UiTextRow(RowHeight, () => _rowClicked(rowIndex));
            _viewer.AddChild(row.Visual);
            _rows.Add(row);
        }

        _viewer.VerticalScrollBarValue = Math.Min(previousScroll,
            _viewer.VerticalScrollBarMaximum);
    }

    private static void StyleScrollBar(ScrollViewerVisual viewer)
    {
        var bar = viewer.VerticalScrollBarInstance as ScrollBarVisual
            ?? throw new InvalidOperationException("Gum V3 vertical scrollbar is required.");

        // Keep Gum's draggable thumb and wheel behavior; replace its broad V3 skin.
        bar.Width = 10f;
        if (bar.UpButtonInstance is { } up)
        {
            up.Visible = false;
            up.Height = 0f;
        }
        if (bar.DownButtonInstance is { } down)
        {
            down.Visible = false;
            down.Height = 0f;
        }
        bar.ThumbContainer.Y = 0f;
        bar.ThumbContainer.Height = 0f;
        bar.ThumbContainer.HeightUnits = DimensionUnitType.RelativeToParent;
        bar.TrackInstance.Width = 2f;
        bar.TrackInstance.WidthUnits = DimensionUnitType.Absolute;
        bar.TrackInstance.X = 4f;
        bar.TrackBackgroundColor = new Color(58, 82, 96, 150);
        bar.ThumbInstance.Width = 6f;
        bar.ThumbInstance.WidthUnits = DimensionUnitType.Absolute;
        bar.ThumbInstance.X = 2f;
        bar.ThumbInstance.BackgroundColor = new Color(139, 181, 204, 225);
        bar.ThumbInstance.FocusedIndicator.Visible = false;
    }
}

using System.Globalization;
using System.Reflection;
using Gum.GueDeriving;
using Gum.Managers;
using Gum.Wireframe;
using Microsoft.Xna.Framework;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Rendering.ThreeD;

namespace MonoGame_project.Game.UI;

/// <summary>Inspect reticle and a virtualized, read-only component inspector.</summary>
public sealed class InspectHudView : IDisposable
{
    private const int VisibleRows = 19;
    private const float PanelRightMargin = 20f;
    private const float PanelTopMargin = 20f;
    private const float ListLeftInset = 18f;
    private const float ListTopInset = 182f;
    private const float ListWidth = 322f;
    private const float RowHeight = 21f;
    private static readonly Color HeadingColor = new(128, 223, 241);
    private static readonly Color TextColor = new(203, 212, 223);
    private static readonly Color MutedColor = new(137, 151, 163);
    private static readonly Dictionary<Type, MemberInfo[]> MemberCache = new();

    private readonly ContainerRuntime _root = new();
    private readonly CircleRuntime _reticle;
    private readonly ContainerRuntime _panel;
    private readonly TextRuntime _entityText;
    private readonly TextRuntime _distanceText;
    private readonly TextRuntime _positionText;
    private readonly TextRuntime _meshText;
    private readonly TextRuntime _componentCountText;
    private readonly TextRuntime _footerText;
    private readonly TextRuntime[] _rows = new TextRuntime[VisibleRows];
    private readonly List<InspectorRow> _allRows = [];
    private int _scrollOffset;
    private Entity _displayedEntity = Entity.None;
    private bool _disposed;

    public InspectHudView(GameUiTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _root.Dock(Dock.Fill);

        _reticle = new CircleRuntime
        {
            Width = 8f,
            Height = 8f,
            IsFilled = true,
            FillColor = new Color(186, 231, 243),
            StrokeWidth = 0f
        };
        _reticle.Anchor(Anchor.Center);
        _root.Children.Add(_reticle);

        _panel = theme.CreatePanel(358f, 640f);
        _panel.Anchor(Anchor.TopRight);
        _panel.X = -PanelRightMargin;
        _panel.Y = PanelTopMargin;
        _root.Children.Add(_panel);

        AddText("OBJECT INSPECTOR", 18f, 16f, 16f, HeadingColor);
        _entityText = AddText("", 18f, 50f, 15f, TextColor);
        _distanceText = AddText("", 18f, 76f, 14f, MutedColor);
        _positionText = AddText("", 18f, 100f, 14f, MutedColor);
        _meshText = AddText("", 18f, 124f, 14f, MutedColor);
        _componentCountText = AddText("", 18f, 153f, 14f, HeadingColor);

        for (var index = 0; index < VisibleRows; index++)
        {
            _rows[index] = AddText("", ListLeftInset, ListTopInset + index * RowHeight, 13f, TextColor);
        }

        _footerText = AddText("", 18f, 605f, 12f, MutedColor);
        _panel.Visible = false;
        _root.Visible = false;
        _root.AddToRoot();
    }

    public bool IsVisible
    {
        get => _root.Visible;
        set => _root.Visible = value;
    }

    public bool IsReticleVisible
    {
        set => _reticle.Visible = value;
    }

    public bool IsCursorOverComponentList(Point mousePosition, int viewportWidth)
    {
        if (!_root.Visible || !_panel.Visible)
        {
            return false;
        }

        var left = viewportWidth - PanelRightMargin - _panel.Width + ListLeftInset;
        var top = PanelTopMargin + ListTopInset;
        return mousePosition.X >= left && mousePosition.X < left + ListWidth &&
            mousePosition.Y >= top && mousePosition.Y < top + VisibleRows * RowHeight;
    }

    public void ShowSelection(
        Entity entity,
        Vector3 cameraPosition,
        Transform3D transform,
        MeshRenderer renderer,
        IReadOnlyList<ComponentSnapshot> components,
        int scrollSteps)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_displayedEntity != entity)
        {
            _displayedEntity = entity;
            _scrollOffset = 0;
        }

        _panel.Visible = true;
        SetText(_entityText, $"Entity {entity.Id}  /  generation {entity.Generation}");
        SetText(_distanceText, $"Distance  {Format(Vector3.Distance(cameraPosition, transform.Position))} m");
        SetText(_positionText, $"Position  {Format(transform.Position)}");
        SetText(_meshText, $"Mesh  {renderer.Mesh.VertexCount} vertices / {renderer.Mesh.PrimitiveCount} triangles");
        SetText(_componentCountText, $"COMPONENTS  {components.Count}");

        BuildRows(components);
        _scrollOffset = Math.Clamp(_scrollOffset - scrollSteps, 0, Math.Max(0, _allRows.Count - VisibleRows));
        for (var index = 0; index < VisibleRows; index++)
        {
            var rowIndex = _scrollOffset + index;
            var row = rowIndex < _allRows.Count ? _allRows[rowIndex] : default;
            SetText(_rows[index], row.Text ?? "");
            var color = row.IsHeading ? HeadingColor : TextColor;
            if (_rows[index].Color != color)
            {
                _rows[index].Color = color;
            }
        }

        SetText(_footerText, _allRows.Count > VisibleRows
            ? $"CTRL + HOVER + WHEEL  {_scrollOffset + 1}-{Math.Min(_scrollOffset + VisibleRows, _allRows.Count)}/{_allRows.Count}"
            : "HOLD LEFT CTRL FOR CURSOR");
    }

    public void ClearSelection()
    {
        _displayedEntity = Entity.None;
        _panel.Visible = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _root.RemoveFromRoot();
        _disposed = true;
    }

    private TextRuntime AddText(string value, float x, float y, float size, Color color)
    {
        var text = new TextRuntime
        {
            Text = value,
            X = x,
            Y = y,
            Width = 322f,
            Height = 21f,
            FontSize = size,
            UseFontSmoothing = true,
            Color = color,
            IsTruncatingWithEllipsisOnLastLine = true
        };
        _panel.Children.Add(text);
        return text;
    }

    private void BuildRows(IReadOnlyList<ComponentSnapshot> components)
    {
        _allRows.Clear();
        var ordered = components.OrderBy(component => component.Type.Name);
        foreach (var component in ordered)
        {
            _allRows.Add(new InspectorRow(component.Type.Name, true));
            var members = GetMembers(component.Type);
            if (members.Length == 0)
            {
                _allRows.Add(new InspectorRow("  (no public data)", false));
            }

            foreach (var member in members)
            {
                object? value = member switch
                {
                    FieldInfo field => field.GetValue(component.Value),
                    PropertyInfo property => property.GetValue(component.Value),
                    _ => null
                };
                _allRows.Add(new InspectorRow($"  {member.Name}: {Format(value)}", false));
            }
        }
    }

    private static MemberInfo[] GetMembers(Type type)
    {
        if (MemberCache.TryGetValue(type, out var members))
        {
            return members;
        }

        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
        members = fields.Length > 0
            ? fields.Cast<MemberInfo>().ToArray()
            : type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                .Cast<MemberInfo>()
                .ToArray();
        MemberCache.Add(type, members);
        return members;
    }

    private static string Format(object? value) => value switch
    {
        null => "null",
        float number => number.ToString("0.###", CultureInfo.InvariantCulture),
        double number => number.ToString("0.###", CultureInfo.InvariantCulture),
        Vector2 v => $"({Format(v.X)}, {Format(v.Y)})",
        Vector3 v => $"({Format(v.X)}, {Format(v.Y)}, {Format(v.Z)})",
        Quaternion q => $"({Format(q.X)}, {Format(q.Y)}, {Format(q.Z)}, {Format(q.W)})",
        Color c => $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}",
        Mesh mesh => $"Mesh ({mesh.VertexCount}v, {mesh.PrimitiveCount}t)",
        bool flag => flag ? "true" : "false",
        _ => value.ToString() ?? ""
    };

    private static void SetText(TextRuntime target, string value)
    {
        if (target.Text != value)
        {
            target.Text = value;
        }
    }

    private readonly record struct InspectorRow(string? Text, bool IsHeading);
}

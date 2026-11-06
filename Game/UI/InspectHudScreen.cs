using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Gum.DataTypes;
using Gum.Forms.Controls;
using Gum.GueDeriving;
using Gum.Managers;
using Gum.Wireframe;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoGame_project.Ecs;
using MonoGame_project.Engine;
using MonoGame_project.Engine.Input;
using MonoGame_project.Engine.Physics.Components;
using MonoGame_project.Engine.Rendering.ThreeD;
using MonoGame_project.Engine.Terrain;
using MonoGame_project.Engine.UI;
using MonoGame_project.Game.Terrain;

namespace MonoGame_project.Game.UI;

/// <summary>Small selection card and optional details; picking stays in InspectSystem.</summary>
internal sealed class InspectHudScreen : UiScreen
{
    private const int MaxVisibleRows = 16;
    private const float Margin = 16f;
    private const float SelectionWidth = 306f;
    private const float SelectionHeight = 68f;
    private const float PanelWidth = 370f;
    private const float PanelMaxHeight = 620f;
    private const float DiagnosticsWidthReserve = 294f;
    private const float RowHeight = 28f;
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan RemovedNoticeDuration = TimeSpan.FromSeconds(2);
    private static readonly Dictionary<Type, MemberInfo[]> MemberCache = new();

    private readonly EngineContext _context;
    private readonly World _world;
    private readonly InspectSessionState _session;
    private readonly ContainerRuntime _root = new();
    private readonly CircleRuntime _reticle;
    private readonly ContainerRuntime _selection;
    private readonly TextRuntime _selectionTitle;
    private readonly Button _detailsButton;
    private readonly ContainerRuntime _panel;
    private readonly ContainerRuntime _summary;
    private readonly ContainerRuntime _listViewport;
    private readonly TextRuntime _title;
    private readonly TextRuntime _distance;
    private readonly TextRuntime _position;
    private readonly TextRuntime _state;
    private readonly TextRuntime _listHeading;
    private readonly TextRuntime _footer;
    private readonly RectangleRuntime _scrollTrack;
    private readonly RectangleRuntime _scrollThumb;
    private readonly Button _closeButton;
    private readonly TextRuntime[] _rowViews = new TextRuntime[MaxVisibleRows];
    private readonly List<InspectorRow> _rows = [];
    private readonly List<Type> _componentTypes = [];
    private readonly HashSet<Type> _expandedComponents = [];
    private readonly Dictionary<Type, object> _snapshotValues = [];

    private Entity _displayedEntity = Entity.None;
    private int _viewportHeight;
    private int _viewportWidth;
    private int _visibleRowCount;
    private int _scrollOffset;
    private int _hoveredRow = -1;
    private int _wheelRemainder;
    private int _knownStructureVersion = -1;
    private long _lastSnapshotAt;
    private long _removedAt;
    private bool _diagnosticsExpanded;
    private bool _detailsOpen;
    private bool _rowsDirty = true;
    private bool _disposed;

    public InspectHudScreen(EngineContext context, World world,
        InspectSessionState session, GameUiTheme theme)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentNullException.ThrowIfNull(theme);
        _root.Dock(Dock.Fill);
        _root.Visible = false;

        _reticle = new CircleRuntime
        {
            Width = 8f,
            Height = 8f,
            IsFilled = true,
            FillColor = GameUiTheme.TextPrimary,
            StrokeWidth = 0f
        };
        _reticle.Anchor(Anchor.Center);
        _root.Children.Add(_reticle);

        // The selection card stays small; the detailed panel is opt-in.
        _selection = theme.CreatePanel(SelectionWidth, SelectionHeight);
        _selection.Anchor(Anchor.TopRight);
        _selection.X = -Margin;
        _selection.Y = Margin;
        _selection.Visible = false;
        _root.Children.Add(_selection);
        var selectionContent = new ContainerRuntime
        {
            X = 16f,
            Y = 15f,
            Width = -32f,
            WidthUnits = DimensionUnitType.RelativeToParent,
            Height = 38f,
            ChildrenLayout = ChildrenLayout.LeftToRightStack,
            StackSpacing = 8f
        };
        _selection.Children.Add(selectionContent);
        _selectionTitle = NewText(15f, GameUiTheme.TextPrimary, 38f);
        _selectionTitle.Width = 1f;
        _selectionTitle.WidthUnits = DimensionUnitType.Ratio;
        selectionContent.Children.Add(_selectionTitle);
        _detailsButton = theme.CreateButton("Details", 86f, 38f);
        _detailsButton.Click += OnDetailsClicked;
        selectionContent.AddChild(_detailsButton);

        _panel = theme.CreatePanel(PanelWidth, PanelMaxHeight);
        _panel.Anchor(Anchor.TopRight);
        _panel.X = -Margin;
        _panel.Y = Margin;
        _panel.Visible = false;
        _root.Children.Add(_panel);
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
        _title = NewText(19f, GameUiTheme.TextPrimary, 36f);
        _title.Width = 1f;
        _title.WidthUnits = DimensionUnitType.Ratio;
        header.Children.Add(_title);
        _closeButton = theme.CreateButton("Close", 72f, 36f);
        _closeButton.Click += OnCloseClicked;
        header.AddChild(_closeButton);

        _summary = new ContainerRuntime
        {
            Width = 0f,
            WidthUnits = DimensionUnitType.RelativeToParent,
            Height = 94f,
            ChildrenLayout = ChildrenLayout.TopToBottomStack,
            StackSpacing = 4f
        };
        content.Children.Add(_summary);
        _distance = NewText(15f, GameUiTheme.TextSecondary, 22f);
        _position = NewText(15f, GameUiTheme.TextSecondary, 22f);
        _state = NewText(15f, GameUiTheme.TextSecondary, 42f);
        _summary.Children.Add(_distance);
        _summary.Children.Add(_position);
        _summary.Children.Add(_state);

        _listHeading = NewText(14f, GameUiTheme.Accent, 20f);
        _listHeading.Text = "Details";
        content.Children.Add(_listHeading);

        _listViewport = new ContainerRuntime
        {
            Width = 0f,
            WidthUnits = DimensionUnitType.RelativeToParent,
            Height = 1f,
            HeightUnits = DimensionUnitType.Ratio
        };
        content.Children.Add(_listViewport);
        var rowStack = new ContainerRuntime
        {
            Width = -14f,
            WidthUnits = DimensionUnitType.RelativeToParent,
            Height = 0f,
            HeightUnits = DimensionUnitType.RelativeToChildren,
            ChildrenLayout = ChildrenLayout.TopToBottomStack
        };
        _listViewport.Children.Add(rowStack);

        _scrollTrack = new RectangleRuntime
        {
            Width = 4f,
            Height = 0f,
            HeightUnits = DimensionUnitType.RelativeToParent,
            FillColor = new Color(57, 66, 72),
            StrokeWidth = 0f,
            Visible = false
        };
        _scrollTrack.Anchor(Anchor.TopRight);
        _listViewport.Children.Add(_scrollTrack);
        _scrollThumb = new RectangleRuntime
        {
            Width = 4f,
            FillColor = GameUiTheme.TextSecondary,
            StrokeWidth = 0f,
            Visible = false
        };
        _scrollTrack.Children.Add(_scrollThumb);
        for (var index = 0; index < _rowViews.Length; index++)
        {
            var row = NewText(14f, GameUiTheme.TextPrimary, RowHeight);
            row.Visible = false;
            rowStack.Children.Add(row);
            _rowViews[index] = row;
        }

        _footer = NewText(12f, GameUiTheme.TextSecondary, 20f);
        content.Children.Add(_footer);
    }

    public override GraphicalUiElement Visual => _root;

    // Input: I toggles the panel; Ctrl releases the cursor for clicking rows.
    // UI input runs before ECS picking. It only changes local presentation state.
    public override void HandleInput()
    {
        if (!InputContexts.CanUseInspect(_context.InputContexts) ||
            !_context.Input.HasActiveInput)
        {
            return;
        }

        if (_context.Input.WasKeyPressed(Keys.I) && _world.IsAlive(_session.Selected))
        {
            SetDetailsOpen(!_detailsOpen);
        }

        if (!_detailsOpen || !_panel.Visible ||
            _context.InputContexts.Current != InputContexts.InspectCursor ||
            _context.Input.CurrentMouse.LeftButton != ButtonState.Pressed ||
            _context.Input.PreviousMouse.LeftButton != ButtonState.Released ||
            !TryGetRowIndex(_context.Input.CurrentMouse.Position, out var rowIndex))
        {
            return;
        }

        var row = _rows[rowIndex];
        if (row.Kind == RowKind.DiagnosticsHeader)
        {
            _diagnosticsExpanded = !_diagnosticsExpanded;
        }
        else if (row.Kind == RowKind.ComponentHeader && row.ComponentType is { } type)
        {
            if (!_expandedComponents.Add(type))
            {
                _expandedComponents.Remove(type);
            }
        }
        else
        {
            return;
        }

        RebuildRows();
    }

    public override void Refresh()
    {
        UpdateLayout();
        var cameraEntity = Entity.None;
        var active = InputContexts.CanUseInspect(_context.InputContexts) &&
            InspectCameraControlSystem.TryGetControlledCamera(_world, out cameraEntity);
        _root.Visible = active;
        _reticle.Visible = _context.InputContexts.Current == InputContexts.InspectViewport;
        if (!active)
        {
            _snapshotValues.Clear();
            _lastSnapshotAt = 0;
            return;
        }

        if (!_world.IsAlive(_session.Selected) ||
            !_world.TryGet<Transform3D>(_session.Selected, out var transform) ||
            !_world.TryGet<MeshRenderer>(_session.Selected, out var renderer) ||
            !renderer.IsVisible || renderer.Mesh is null || renderer.Mesh.IsDisposed)
        {
            ShowNoSelection();
            return;
        }

        var entity = _session.Selected;
        if (_displayedEntity != entity)
        {
            ResetForSelection(entity);
        }

        SetText(_selectionTitle, GetObjectLabel(entity));
        _detailsButton.Visual.Visible = true;
        _selection.Visible = !_detailsOpen;
        _panel.Visible = _detailsOpen;
        if (!_detailsOpen)
        {
            return;
        }

        UpdateVisibleRows();
        UpdateSummary(entity, _world.Get<Transform3D>(cameraEntity).Position, transform);
        var sampled = RefreshSnapshotsIfDue(entity);
        var scrolled = ApplyWheelInput();
        var hoveredRow = _context.InputContexts.Current == InputContexts.InspectCursor &&
            TryGetRowIndex(_context.Input.CurrentMouse.Position, out var index) &&
            _rows[index].Kind is RowKind.DiagnosticsHeader or RowKind.ComponentHeader
            ? index : -1;
        if (_hoveredRow != hoveredRow)
        {
            _hoveredRow = hoveredRow;
            _rowsDirty = true;
        }

        if (sampled || scrolled || _rowsDirty)
        {
            RenderRows(entity, renderer);
            _rowsDirty = false;
        }
    }

    // Gum owns the child layout; only the outer card size changes with the viewport.
    private void UpdateLayout()
    {
        var viewport = _context.GraphicsDevice.Viewport;
        if (_viewportWidth == viewport.Width && _viewportHeight == viewport.Height)
        {
            return;
        }

        _viewportWidth = viewport.Width;
        _viewportHeight = viewport.Height;
        _selection.Width = MathF.Min(SelectionWidth, MathF.Max(1f, viewport.Width - 2f * Margin));
        var widthBesideDiagnostics = viewport.Width - DiagnosticsWidthReserve - Margin;
        var availableWidth = widthBesideDiagnostics >= 200f
            ? widthBesideDiagnostics
            : viewport.Width - 2f * Margin;
        _panel.Width = MathF.Min(PanelWidth, MathF.Max(1f, availableWidth));
        _panel.Height = MathF.Min(PanelMaxHeight, MathF.Max(1f, viewport.Height - 2f * Margin));
        // At short heights the summary yields space to the scrollable data area.
        _summary.Visible = _panel.Height >= 300f;
        _listHeading.Visible = _panel.Height >= 160f;
        _footer.Visible = _panel.Height >= 190f;

        _rowsDirty = true;
    }

    private void UpdateVisibleRows()
    {
        var count = Math.Clamp(
            (int)MathF.Floor(_listViewport.AbsoluteHeight / RowHeight),
            1, MaxVisibleRows);
        if (count == _visibleRowCount)
        {
            return;
        }

        _visibleRowCount = count;
        _scrollOffset = Math.Clamp(_scrollOffset, 0,
            Math.Max(0, _rows.Count - _visibleRowCount));
        _rowsDirty = true;
    }

    private void ShowNoSelection()
    {
        if (!_displayedEntity.IsNone)
        {
            _removedAt = !_world.IsAlive(_displayedEntity)
                ? Stopwatch.GetTimestamp() : 0;
            _displayedEntity = Entity.None;
        }

        SetDetailsOpen(false);
        _panel.Visible = false;
        var showRemoved = _removedAt != 0 &&
            Stopwatch.GetElapsedTime(_removedAt) < RemovedNoticeDuration;
        _selection.Visible = showRemoved;
        if (showRemoved)
        {
            SetText(_selectionTitle, "Object removed");
            _detailsButton.Visual.Visible = false;
        }
        else
        {
            _removedAt = 0;
        }
    }

    private void ResetForSelection(Entity entity)
    {
        _displayedEntity = entity;
        _removedAt = 0;
        _scrollOffset = 0;
        _hoveredRow = -1;
        _wheelRemainder = 0;
        _knownStructureVersion = -1;
        _lastSnapshotAt = 0;
        _diagnosticsExpanded = false;
        _expandedComponents.Clear();
        _componentTypes.Clear();
        _snapshotValues.Clear();
        _rows.Clear();
        _rowsDirty = true;
    }

    private void UpdateSummary(Entity entity, Vector3 cameraPosition,
        Transform3D transform)
    {
        SetText(_title, GetObjectLabel(entity));
        if (_world.TryGet<TerrainChunkTag>(entity, out var terrain))
        {
            var lod = _world.TryGet<TerrainRenderLodComponent>(entity, out var renderLod)
                ? renderLod.Value.ToString() : "not loaded";
            SetText(_distance, $"Render LOD  {lod}");
            SetText(_position, $"Chunk  ({terrain.ChunkX}, {terrain.ChunkZ})");
            SetText(_state, _world.Has<TerrainCollider3D>(entity)
                ? "Heightfield collision  loaded" : "Heightfield collision  not loaded");
        }
        else
        {
            SetText(_distance,
                $"Distance  {Format(Vector3.Distance(cameraPosition, transform.Position))} m");
            SetText(_position,
                $"Position  {FormatCompact(transform.Position.X)}, " +
                $"{FormatCompact(transform.Position.Y)}, " +
                $"{FormatCompact(transform.Position.Z)} m");
            SetText(_state, _world.TryGet<PhysicsBody3D>(entity, out var body)
                ? $"Physics  {body.MotionType} · {GetColliderLabel(entity)}"
                : "No physics body");
        }
    }

    // Component list: cache its structure, sample values, and render visible rows.
    private bool RefreshSnapshotsIfDue(Entity entity)
    {
        // Reading boxed component snapshots and reflecting fields every rendered
        // frame is unnecessary for a diagnostic panel. Visible values refresh at 10 Hz.
        var now = Stopwatch.GetTimestamp();
        if (_lastSnapshotAt != 0 &&
            Stopwatch.GetElapsedTime(_lastSnapshotAt, now) < SnapshotInterval)
        {
            return false;
        }

        _lastSnapshotAt = now;
        var snapshots = _world.GetComponentSnapshots(entity);
        _snapshotValues.Clear();
        foreach (var snapshot in snapshots)
        {
            _snapshotValues.Add(snapshot.Type, snapshot.Value);
        }

        if (_knownStructureVersion != _world.StructureVersion)
        {
            _knownStructureVersion = _world.StructureVersion;
            var types = _snapshotValues.Keys.OrderBy(type => type.Name).ToArray();
            if (!_componentTypes.SequenceEqual(types))
            {
                _componentTypes.Clear();
                _componentTypes.AddRange(types);
                _expandedComponents.RemoveWhere(type => !_componentTypes.Contains(type));
                RebuildRows();
            }
        }

        return true;
    }

    private void RebuildRows()
    {
        _rows.Clear();
        _rows.Add(new InspectorRow(RowKind.DiagnosticsHeader, "Technical details"));
        if (_diagnosticsExpanded)
        {
            _rows.Add(new InspectorRow(RowKind.Diagnostic, "Entity ID",
                Diagnostic: DiagnosticField.EntityId));
            _rows.Add(new InspectorRow(RowKind.Diagnostic, "Generation",
                Diagnostic: DiagnosticField.Generation));
            _rows.Add(new InspectorRow(RowKind.Diagnostic, "Vertices",
                Diagnostic: DiagnosticField.Vertices));
            _rows.Add(new InspectorRow(RowKind.Diagnostic, "Triangles",
                Diagnostic: DiagnosticField.Triangles));
        }

        _rows.Add(new InspectorRow(RowKind.SectionHeader,
            $"Components ({_componentTypes.Count})"));
        foreach (var type in _componentTypes)
        {
            _rows.Add(new InspectorRow(RowKind.ComponentHeader, type.Name, type));
            if (!_expandedComponents.Contains(type))
            {
                continue;
            }

            var members = GetMembers(type);
            if (members.Length == 0)
            {
                _rows.Add(new InspectorRow(RowKind.ComponentValue, "(no public data)", type));
            }
            else
            {
                foreach (var member in members)
                {
                    _rows.Add(new InspectorRow(RowKind.ComponentValue,
                        member.Name, type, member));
                }
            }
        }

        _scrollOffset = Math.Clamp(_scrollOffset, 0,
            Math.Max(0, _rows.Count - _visibleRowCount));
        _rowsDirty = true;
    }

    private bool ApplyWheelInput()
    {
        if (_context.InputContexts.Current != InputContexts.InspectCursor ||
            !IsCursorOverList(_context.Input.CurrentMouse.Position))
        {
            _wheelRemainder = 0;
            return false;
        }

        _wheelRemainder += _context.Input.CurrentMouse.ScrollWheelValue -
            _context.Input.PreviousMouse.ScrollWheelValue;
        var steps = _wheelRemainder / 120;
        _wheelRemainder %= 120;
        if (steps == 0)
        {
            return false;
        }

        var previous = _scrollOffset;
        _scrollOffset = Math.Clamp(_scrollOffset - steps, 0,
            Math.Max(0, _rows.Count - _visibleRowCount));
        return _scrollOffset != previous;
    }

    private bool IsCursorOverList(Point mouse) =>
        _panel.Visible && Contains(_listViewport, mouse);

    private bool TryGetRowIndex(Point mouse, out int index)
    {
        index = -1;
        if (!IsCursorOverList(mouse))
        {
            return false;
        }

        for (var slot = 0; slot < _visibleRowCount; slot++)
        {
            if (_rowViews[slot].Visible && Contains(_rowViews[slot], mouse))
            {
                index = _scrollOffset + slot;
                return index < _rows.Count;
            }
        }

        return false;
    }

    private static bool Contains(GraphicalUiElement visual, Point mouse) =>
        mouse.X >= visual.AbsoluteLeft &&
        mouse.X < visual.AbsoluteLeft + visual.AbsoluteWidth &&
        mouse.Y >= visual.AbsoluteTop &&
        mouse.Y < visual.AbsoluteTop + visual.AbsoluteHeight;

    private void RenderRows(Entity entity, MeshRenderer renderer)
    {
        for (var slot = 0; slot < _rowViews.Length; slot++)
        {
            var view = _rowViews[slot];
            var index = _scrollOffset + slot;
            if (slot >= _visibleRowCount || index >= _rows.Count)
            {
                view.Visible = false;
                continue;
            }

            var row = _rows[index];
            view.Visible = true;
            SetText(view, FormatRow(row, entity, renderer));
            view.Color = index == _hoveredRow ? GameUiTheme.Accent :
                row.Kind == RowKind.ComponentValue || row.Kind == RowKind.Diagnostic
                    ? GameUiTheme.TextSecondary : GameUiTheme.TextPrimary;
        }

        var overflow = _rows.Count > _visibleRowCount;
        _scrollTrack.Visible = overflow;
        _scrollThumb.Visible = overflow;
        if (overflow)
        {
            var trackHeight = _scrollTrack.AbsoluteHeight;
            _scrollThumb.Height = MathF.Min(trackHeight,
                MathF.Max(24f, trackHeight * _visibleRowCount / _rows.Count));
            _scrollThumb.Y = (trackHeight - _scrollThumb.Height) *
                _scrollOffset / (_rows.Count - _visibleRowCount);
        }

        var range = _rows.Count == 0 ? "0" :
            $"{_scrollOffset + 1}-{Math.Min(_scrollOffset + _visibleRowCount, _rows.Count)}/{_rows.Count}";
        SetText(_footer, $"{range}  ·  Ctrl + wheel  ·  I close");
    }

    private string FormatRow(InspectorRow row, Entity entity, MeshRenderer renderer)
    {
        switch (row.Kind)
        {
            case RowKind.DiagnosticsHeader:
                return $"{(_diagnosticsExpanded ? "-" : "+")}  {row.Label}";
            case RowKind.SectionHeader:
                return row.Label;
            case RowKind.ComponentHeader:
                return $"{(_expandedComponents.Contains(row.ComponentType!) ? "-" : "+")}  {row.Label}";
            case RowKind.Diagnostic:
                return row.Diagnostic switch
                {
                    DiagnosticField.EntityId => $"  Entity ID  {entity.Id}",
                    DiagnosticField.Generation => $"  Generation  {entity.Generation}",
                    DiagnosticField.Vertices => $"  Vertices  {renderer.Mesh.VertexCount:N0}",
                    DiagnosticField.Triangles => $"  Triangles  {renderer.Mesh.PrimitiveCount:N0}",
                    _ => ""
                };
            case RowKind.ComponentValue:
                if (row.Member is null)
                {
                    return "  (no public data)";
                }

                if (!_snapshotValues.TryGetValue(row.ComponentType!, out var component))
                {
                    return $"  {row.Label}  —";
                }

                try
                {
                    var value = row.Member switch
                    {
                        FieldInfo field => field.GetValue(component),
                        PropertyInfo property => property.GetValue(component),
                        _ => null
                    };
                    return $"  {row.Label}  {Format(value)}";
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // A diagnostic getter must not bring down the game loop.
                    return $"  {row.Label}  <unavailable>";
                }
            default:
                return "";
        }
    }

    // Labels and formatting do not add UI-specific components to the world.
    private string GetObjectLabel(Entity entity)
    {
        if (_world.TryGet<TerrainChunkTag>(entity, out var terrain))
        {
            return $"Terrain ({terrain.ChunkX}, {terrain.ChunkZ})";
        }

        return $"{GetColliderLabel(entity)} #{entity.Id}";
    }

    private string GetColliderLabel(Entity entity) =>
        _world.Has<SphereCollider3D>(entity) ? "Sphere" :
        _world.Has<BoxCollider3D>(entity) ? "Box" :
        _world.Has<CapsuleCollider3D>(entity) ? "Capsule" :
        _world.Has<CylinderCollider3D>(entity) ? "Cylinder" :
        _world.Has<ConeCollider3D>(entity) ? "Cone" : "Object";

    private static MemberInfo[] GetMembers(Type type)
    {
        if (MemberCache.TryGetValue(type, out var members))
        {
            return members;
        }

        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
        members = fields.Length > 0
            ? fields.OrderBy(field => field.Name).Cast<MemberInfo>().ToArray()
            : type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                .OrderBy(property => property.Name)
                .Cast<MemberInfo>().ToArray();
        MemberCache.Add(type, members);
        return members;
    }

    private static TextRuntime NewText(float size, Color color, float height) => new()
    {
        Width = 0f,
        WidthUnits = DimensionUnitType.RelativeToParent,
        Height = height,
        FontSize = size,
        UseFontSmoothing = true,
        Color = color,
        IsTruncatingWithEllipsisOnLastLine = true
    };

    private static void SetText(TextRuntime target, string value)
    {
        if (target.Text != value)
        {
            target.Text = value;
        }
    }

    private static string FormatCompact(float value) =>
        value.ToString("0.#", CultureInfo.InvariantCulture);

    private void OnDetailsClicked(object? sender, EventArgs args)
    {
        if (_context.InputContexts.Current == InputContexts.InspectCursor &&
            _world.IsAlive(_session.Selected))
        {
            SetDetailsOpen(true);
        }
    }

    private void OnCloseClicked(object? sender, EventArgs args)
    {
        if (_context.InputContexts.Current == InputContexts.InspectCursor)
        {
            SetDetailsOpen(false);
        }
    }

    private void SetDetailsOpen(bool open)
    {
        if (_detailsOpen == open)
        {
            return;
        }

        _detailsOpen = open;
        _lastSnapshotAt = 0;
        _rowsDirty = true;
        if (!open)
        {
            _snapshotValues.Clear();
        }
    }

    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _detailsButton.Click -= OnDetailsClicked;
        _closeButton.Click -= OnCloseClicked;
        _disposed = true;
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
        TerrainHeightField heights => $"{heights.CellCount} x {heights.CellCount} cells",
        bool flag => flag ? "true" : "false",
        _ => value.ToString() ?? ""
    };

    private enum RowKind
    {
        DiagnosticsHeader,
        Diagnostic,
        SectionHeader,
        ComponentHeader,
        ComponentValue
    }

    private enum DiagnosticField
    {
        None,
        EntityId,
        Generation,
        Vertices,
        Triangles
    }

    private readonly record struct InspectorRow(
        RowKind Kind,
        string Label,
        Type? ComponentType = null,
        MemberInfo? Member = null,
        DiagnosticField Diagnostic = DiagnosticField.None);
}

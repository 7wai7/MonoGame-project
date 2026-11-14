using System.Diagnostics;
using Gum.Wireframe;
using Microsoft.Xna.Framework.Input;
using MonoGame_project.Ecs;
using MonoGame_project.Engine;
using MonoGame_project.Engine.Input;
using MonoGame_project.Engine.Rendering.ThreeD;
using MonoGame_project.Engine.UI;

namespace MonoGame_project.Game.UI;

/// <summary>Connects the Gum inspector layout to selection and ECS snapshots.</summary>
internal sealed class InspectHudScreen : UiScreen
{
    private static readonly TimeSpan RemovedNoticeDuration = TimeSpan.FromSeconds(2);

    private readonly EngineContext _context;
    private readonly World _world;
    private readonly InspectSessionState _session;
    private readonly GraphicalUiElement _visual;
    private readonly GraphicalUiElement _reticle;
    private readonly GraphicalUiElement _selection;
    private readonly GraphicalUiElement _selectionTitle;
    private readonly GraphicalUiElement _details;
    private readonly GraphicalUiElement _detailTitle;
    private readonly GraphicalUiElement _primary;
    private readonly GraphicalUiElement _secondary;
    private readonly GraphicalUiElement _state;
    private readonly GraphicalUiElement _componentsLabel;
    private readonly GraphicalUiElement _footer;
    private readonly GumHudButton _detailsButton;
    private readonly GumHudButton _closeButton;
    private readonly InspectComponentList _componentList;
    private readonly InspectRowsModel _rows = new();

    private Entity _displayedEntity = Entity.None;
    private long _removedAt;
    private bool _detailsOpen;
    private bool _rowsDirty = true;
    private bool _disposed;

    public InspectHudScreen(EngineContext context, World world,
        InspectSessionState session)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _session = session ?? throw new ArgumentNullException(nameof(session));

        _visual = GumHudProject.CreateScreen("InspectHud");
        _visual.Visible = false;
        _reticle = GumHudProject.Require(_visual, "Reticle");
        _selection = GumHudProject.Require(_visual, "SelectionPanel");
        _selectionTitle = GumHudProject.Require(_visual, "SelectionTitle");
        _details = GumHudProject.Require(_visual, "DetailsPanel");
        _detailTitle = GumHudProject.Require(_visual, "DetailTitle");
        _primary = GumHudProject.Require(_visual, "SummaryPrimary");
        _secondary = GumHudProject.Require(_visual, "SummarySecondary");
        _state = GumHudProject.Require(_visual, "SummaryState");
        _componentsLabel = GumHudProject.Require(_visual, "ComponentsLabel");
        _footer = GumHudProject.Require(_visual, "Footer");
        _detailsButton = new GumHudButton(_visual, "DetailsButton", "DETAILS", OpenDetails);
        _closeButton = new GumHudButton(_visual, "CloseButton", "CLOSE", CloseDetails);
        _componentList = new InspectComponentList(OnRowClicked);
        GumHudProject.Require(_visual, "ListSlot").Children.Add(_componentList.Visual);
    }

    public override GraphicalUiElement Visual => _visual;

    internal void ShowBenchmarkDetails(Entity entity)
    {
        _session.Selected = entity;
        _displayedEntity = entity;
        _rows.Reset();
        _componentList.Reset();
        SetDetailsOpen(true);
        _rows.SampleIfDue(_world, entity);
        _rows.ExpandFirstComponent();
        _rowsDirty = true;
    }

    public override void HandleInput()
    {
        if (InputContexts.CanUseInspect(_context.InputContexts) &&
            _context.Input.HasActiveInput &&
            _context.Input.WasKeyPressed(Keys.I) &&
            _world.IsAlive(_session.Selected))
        {
            SetDetailsOpen(!_detailsOpen);
        }
    }

    public override void Refresh()
    {
        var cameraEntity = Entity.None;
        var active = (InputContexts.CanUseInspect(_context.InputContexts) ||
                      _context.Benchmark is not null) &&
            InspectCameraControlSystem.TryGetControlledCamera(_world, out cameraEntity);
        _visual.Visible = active;
        _reticle.Visible = active &&
            (_context.InputContexts.Current == InputContexts.InspectViewport ||
             _context.Benchmark is { InspectorVisible: false });
        if (!active)
        {
            _rows.ClearSnapshots();
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
            _displayedEntity = entity;
            _removedAt = 0;
            _rows.Reset();
            _componentList.Reset();
            _rowsDirty = true;
        }

        GumHudProject.SetText(_selectionTitle, InspectObjectPresentation.Label(_world, entity));
        _detailsButton.Visible = true;
        _selection.Visible = !_detailsOpen;
        _details.Visible = _detailsOpen;
        if (!_detailsOpen)
        {
            return;
        }

        RefreshResponsiveVisibility();
        var summary = InspectObjectPresentation.Summary(_world, entity,
            _world.Get<Transform3D>(cameraEntity).Position, transform);
        GumHudProject.SetText(_detailTitle, summary.Title);
        GumHudProject.SetText(_primary, summary.Primary);
        GumHudProject.SetText(_secondary, summary.Secondary);
        GumHudProject.SetText(_state, summary.State);
        var sampled = _rows.SampleIfDue(_world, entity);
        if (sampled || _rowsDirty)
        {
            _componentList.SetRows(_rows.Revision, _rows.FormatRows(entity, renderer));
            _rowsDirty = false;
        }
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _detailsButton.Dispose();
        _closeButton.Dispose();
        _componentList.Dispose();
        _disposed = true;
    }

    private void RefreshResponsiveVisibility()
    {
        var height = _details.AbsoluteHeight;
        _primary.Visible = height >= 300f;
        _secondary.Visible = height >= 300f;
        _state.Visible = height >= 300f;
        _componentsLabel.Visible = height >= 260f;
        _footer.Visible = height >= 330f;
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
        _details.Visible = false;
        var showRemoved = _removedAt != 0 &&
            Stopwatch.GetElapsedTime(_removedAt) < RemovedNoticeDuration;
        _selection.Visible = showRemoved;
        if (showRemoved)
        {
            GumHudProject.SetText(_selectionTitle, "OBJECT REMOVED");
            _detailsButton.Visible = false;
        }
        else
        {
            _removedAt = 0;
        }
    }

    private void OpenDetails()
    {
        if (_context.InputContexts.Current == InputContexts.InspectCursor &&
            _world.IsAlive(_session.Selected))
        {
            SetDetailsOpen(true);
        }
    }

    private void CloseDetails()
    {
        if (_context.InputContexts.Current == InputContexts.InspectCursor)
        {
            SetDetailsOpen(false);
        }
    }

    private void OnRowClicked(int index)
    {
        if (_context.InputContexts.Current != InputContexts.InspectCursor ||
            !_detailsOpen || !_world.IsAlive(_session.Selected)) return;

        _rows.ToggleRow(index);
        _rowsDirty = true;
    }

    private void SetDetailsOpen(bool open)
    {
        if (_detailsOpen == open) return;
        _detailsOpen = open;
        _rows.ClearSnapshots();
        _rowsDirty = true;
        if (!open) _details.Visible = false;
    }
}

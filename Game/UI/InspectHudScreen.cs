using System.Diagnostics;
using Gum.DataTypes;
using Gum.GueDeriving;
using Gum.Wireframe;
using Microsoft.Xna.Framework.Input;
using MonoGame_project.Ecs;
using MonoGame_project.Engine;
using MonoGame_project.Engine.Input;
using MonoGame_project.Engine.Rendering.ThreeD;
using MonoGame_project.Engine.UI;

namespace MonoGame_project.Game.UI;

/// <summary>Coordinates inspect UI components; 3D picking stays in InspectSystem.</summary>
internal sealed class InspectHudScreen : UiScreen
{
    private const float SafeAreaInset = 16f;
    private static readonly TimeSpan RemovedNoticeDuration = TimeSpan.FromSeconds(2);

    private readonly EngineContext _context;
    private readonly World _world;
    private readonly InspectSessionState _session;
    private readonly ContainerRuntime _root = new();
    private readonly ContainerRuntime _safeArea = new();
    private readonly InspectReticleView _reticle = new();
    private readonly InspectSelectionCard _selection;
    private readonly InspectDetailsPanel _details;
    private readonly InspectRowsModel _rows = new();

    private Entity _displayedEntity = Entity.None;
    private long _removedAt;
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
        _safeArea.X = SafeAreaInset;
        _safeArea.Y = SafeAreaInset;
        _safeArea.Width = -2f * SafeAreaInset;
        _safeArea.WidthUnits = DimensionUnitType.RelativeToParent;
        _safeArea.Height = -2f * SafeAreaInset;
        _safeArea.HeightUnits = DimensionUnitType.RelativeToParent;
        _safeArea.MinWidth = 1f;
        _safeArea.MinHeight = 1f;
        _selection = new InspectSelectionCard(theme, OpenDetails);
        _details = new InspectDetailsPanel(theme, CloseDetails, OnRowClicked);
        _root.Children.Add(_reticle.Visual);
        _root.Children.Add(_safeArea);
        _safeArea.Children.Add(_selection.Visual);
        _safeArea.Children.Add(_details.Visual);
    }

    public override GraphicalUiElement Visual => _root;

    // UI input precedes ECS picking, so I can change only the panel state here.
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
        var active = InputContexts.CanUseInspect(_context.InputContexts) &&
            InspectCameraControlSystem.TryGetControlledCamera(_world, out cameraEntity);
        _root.Visible = active;
        _reticle.Visible = active &&
            _context.InputContexts.Current == InputContexts.InspectViewport;
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
            _details.ResetRows();
            _rowsDirty = true;
        }

        _selection.Show(InspectObjectPresentation.Label(_world, entity), true);
        _selection.Visible = !_detailsOpen;
        _details.Visible = _detailsOpen;
        if (!_detailsOpen)
        {
            return;
        }

        _details.RefreshResponsiveVisibility();
        _details.SetSummary(InspectObjectPresentation.Summary(_world, entity,
            _world.Get<Transform3D>(cameraEntity).Position, transform));
        var sampled = _rows.SampleIfDue(_world, entity);
        if (sampled || _rowsDirty)
        {
            _details.SetRows(_rows.Revision, _rows.FormatRows(entity, renderer));
            _rowsDirty = false;
        }
    }

    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _selection.Dispose();
        _details.Dispose();
        _disposed = true;
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
            _selection.Show("Object removed", allowDetails: false);
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
            !_detailsOpen || !_world.IsAlive(_session.Selected))
        {
            return;
        }

        _rows.ToggleRow(index);
        _rowsDirty = true;
    }

    private void SetDetailsOpen(bool open)
    {
        if (_detailsOpen == open)
        {
            return;
        }

        _detailsOpen = open;
        _rows.ClearSnapshots();
        _rowsDirty = true;
        if (!open)
        {
            _details.Visible = false;
        }
    }
}

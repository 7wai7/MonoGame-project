using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Input;
using MonoGame_project.Engine.Rendering;
using MonoGame_project.Engine.Rendering.ThreeD;
using MonoGame_project.Engine.Systems;
using MonoGame_project.Engine.Time;
using MonoGame_project.Game.Terrain;
using MonoGame_project.Game.UI;

namespace MonoGame_project.Game;

/// <summary>Owns inspect picking, selection, HUD and the temporary mesh outline.</summary>
public sealed class InspectSystem : GameSystem, IUpdateSystem, IContentSystem, IRenderSystem
{
    private const float OutlineScale = 1.035f;

    private readonly BlendState _stencilOnlyBlend = new()
    {
        ColorWriteChannels = ColorWriteChannels.None
    };
    private readonly DepthStencilState _markVisibleMesh = new()
    {
        DepthBufferEnable = true,
        DepthBufferWriteEnable = false,
        DepthBufferFunction = CompareFunction.LessEqual,
        StencilEnable = true,
        StencilFunction = CompareFunction.Always,
        StencilPass = StencilOperation.Replace,
        ReferenceStencil = 1
    };
    private readonly DepthStencilState _drawOutsideMesh = new()
    {
        DepthBufferEnable = true,
        DepthBufferWriteEnable = false,
        DepthBufferFunction = CompareFunction.LessEqual,
        StencilEnable = true,
        StencilFunction = CompareFunction.NotEqual,
        ReferenceStencil = 1
    };

    private InspectHudView _view = null!;
    private InspectSessionState _session = null!;
    private BasicEffect _outlineEffect = null!;
    private Entity _hovered = Entity.None;
    private bool _contentLoaded;

    protected override void OnInitialize()
    {
        _view = new InspectHudView(World.GetResource<GameUiTheme>());
        _session = new InspectSessionState();
        World.SetResource(_session);
    }

    public void LoadContent(World world, ContentManager content)
    {
        _outlineEffect = new BasicEffect(Context.GraphicsDevice)
        {
            LightingEnabled = false,
            TextureEnabled = false,
            VertexColorEnabled = false,
            DiffuseColor = new Color(245, 224, 117).ToVector3(),
            Alpha = 0.9f
        };
        _contentLoaded = true;
    }

    public void UnloadContent(World world)
    {
        if (_contentLoaded)
        {
            _outlineEffect.Dispose();
            _outlineEffect = null!;
            _contentLoaded = false;
        }
    }

    public void Update(World world, in FrameTime time)
    {
        var cameraEntity = Entity.None;
        var active = InputContexts.CanUseInspect(Context.InputContexts) &&
            InspectCameraControlSystem.TryGetControlledCamera(world, out cameraEntity);
        _view.IsVisible = active;
        _view.IsReticleVisible = Context.InputContexts.Current == InputContexts.InspectViewport;
        _view.IsOrbiting = !_session.OrbitTarget.IsNone;
        _hovered = Entity.None;
        if (!active)
        {
            return;
        }

        var cameraTransform = world.Get<Transform3D>(cameraEntity);
        var camera = world.Get<Camera3D>(cameraEntity);

        if (Context.Input.IsRelativeMouseCaptured && _session.OrbitTarget.IsNone)
        {
            var direction = camera.Target - cameraTransform.Position;
            if (direction.LengthSquared() > 1e-8f)
            {
                direction.Normalize();
                var ray = new Ray(cameraTransform.Position, direction);
                MeshRaycaster.TryPick(world, ray, camera.FarPlane, out _hovered, out _);
            }

            if (Context.Input.WasViewportLeftClicked)
            {
                _session.Selected = _hovered;
            }
        }

        if (!world.IsAlive(_session.Selected) ||
            !world.TryGet<Transform3D>(_session.Selected, out var selectedTransform) ||
            !world.TryGet<MeshRenderer>(_session.Selected, out var selectedRenderer) ||
            !selectedRenderer.IsVisible || selectedRenderer.Mesh is null ||
            selectedRenderer.Mesh.IsDisposed)
        {
            _session.Selected = Entity.None;
            _session.OrbitTarget = Entity.None;
            _view.ClearSelection();
            return;
        }

        var wheelDelta = Context.InputContexts.Current == InputContexts.InspectCursor &&
            _view.IsCursorOverComponentList(
                Context.Input.CurrentMouse.Position,
                Context.Window.ClientBounds.Width)
            ? Context.Input.CurrentMouse.ScrollWheelValue -
                Context.Input.PreviousMouse.ScrollWheelValue
            : 0;
        _view.ShowSelection(
            _session.Selected,
            cameraTransform.Position,
            selectedTransform,
            selectedRenderer,
            world.GetComponentSnapshots(_session.Selected),
            wheelDelta / 120);
    }

    public void Draw(World world, in RenderContext context)
    {
        if (!_contentLoaded || !_view.IsVisible ||
            !MeshRenderSystem.TryGetActiveCamera(world, out var cameraTransform, out var camera))
        {
            return;
        }

        var outlined = world.IsAlive(_hovered) ? _hovered : _session.Selected;
        if (!world.IsAlive(outlined) ||
            world.Has<TerrainChunkTag>(outlined) ||
            !world.TryGet<Transform3D>(outlined, out var transform) ||
            !world.TryGet<MeshRenderer>(outlined, out var renderer) ||
            !renderer.IsVisible || renderer.Mesh is null || renderer.Mesh.IsDisposed)
        {
            return;
        }

        var device = context.GraphicsDevice;
        var oldBlend = device.BlendState;
        var oldDepth = device.DepthStencilState;
        var oldRasterizer = device.RasterizerState;
        try
        {
            device.Clear(ClearOptions.Stencil, Color.Transparent, 1f, 0);
            device.RasterizerState = RasterizerState.CullNone;
            _outlineEffect.View = Matrix.CreateLookAt(cameraTransform.Position, camera.Target, camera.Up);
            _outlineEffect.Projection = Matrix.CreatePerspectiveFieldOfView(
                camera.FieldOfView,
                device.Viewport.AspectRatio,
                camera.NearPlane,
                camera.FarPlane);

            var worldMatrix = MeshRenderSystem.GetRenderWorldMatrix(
                world, outlined, transform, context.InterpolationAlpha);

            // Mark only the mesh's visible pixels. The enlarged second pass is
            // drawn outside that mask, while the depth buffer hides occluded parts.
            device.BlendState = _stencilOnlyBlend;
            device.DepthStencilState = _markVisibleMesh;
            DrawMesh(device, renderer.Mesh, worldMatrix);

            device.BlendState = BlendState.NonPremultiplied;
            device.DepthStencilState = _drawOutsideMesh;
            var center = (renderer.Mesh.SourceData.Bounds.Min +
                renderer.Mesh.SourceData.Bounds.Max) * 0.5f;
            var expanded = Matrix.CreateTranslation(-center) *
                Matrix.CreateScale(OutlineScale) *
                Matrix.CreateTranslation(center) * worldMatrix;
            DrawMesh(device, renderer.Mesh, expanded);
        }
        finally
        {
            device.BlendState = oldBlend;
            device.DepthStencilState = oldDepth;
            device.RasterizerState = oldRasterizer;
        }
    }

    protected override void OnShutdown()
    {
        World.RemoveResource<InspectSessionState>();
        _session = null!;
        _view.Dispose();
        _view = null!;
        _stencilOnlyBlend.Dispose();
        _markVisibleMesh.Dispose();
        _drawOutsideMesh.Dispose();
    }

    private void DrawMesh(GraphicsDevice device, Mesh mesh, Matrix worldMatrix)
    {
        _outlineEffect.World = worldMatrix;
        foreach (var pass in _outlineEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            mesh.Draw(device);
        }
    }
}

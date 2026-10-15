using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Physics.Components;
using MonoGame_project.Engine.Rendering;
using MonoGame_project.Engine.Rendering.ThreeD;
using MonoGame_project.Engine.Systems;

namespace MonoGame_project.Engine.Physics;

/// <summary>Draws supported colliders as wireframes for development diagnostics.</summary>
public sealed class PhysicsDebugRenderSystem : GameSystem, IContentSystem, IRenderSystem
{
    private static readonly Vector3[] BoxCorners =
    [
        new(-0.5f, -0.5f, -0.5f),
        new( 0.5f, -0.5f, -0.5f),
        new( 0.5f,  0.5f, -0.5f),
        new(-0.5f,  0.5f, -0.5f),
        new(-0.5f, -0.5f,  0.5f),
        new( 0.5f, -0.5f,  0.5f),
        new( 0.5f,  0.5f,  0.5f),
        new(-0.5f,  0.5f,  0.5f)
    ];

    private static readonly short[] BoxIndices =
    [
        0, 1, 1, 2, 2, 3, 3, 0,
        4, 5, 5, 6, 6, 7, 7, 4,
        0, 4, 1, 5, 2, 6, 3, 7
    ];

    private readonly VertexPositionColor[] _vertices = new VertexPositionColor[BoxCorners.Length];
    private BasicEffect _effect = null!;
    private bool _contentLoaded;

    public bool IsVisible { get; set; } = true;
    public Color Color { get; set; } = Color.LimeGreen;

    public void LoadContent(World world, ContentManager content)
    {
        _effect = new BasicEffect(Context.GraphicsDevice)
        {
            LightingEnabled = false,
            TextureEnabled = false,
            VertexColorEnabled = true,
            World = Matrix.Identity
        };
        _contentLoaded = true;
    }

    public void UnloadContent(World world)
    {
        if (!_contentLoaded)
        {
            return;
        }

        _effect.Dispose();
        _effect = null!;
        _contentLoaded = false;
    }

    public void Draw(World world, in RenderContext context)
    {
        if (!_contentLoaded || !IsVisible ||
            !TryGetActiveCamera(world, out var cameraTransform, out var camera))
        {
            return;
        }

        var graphicsDevice = context.GraphicsDevice;
        graphicsDevice.BlendState = BlendState.Opaque;
        graphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
        graphicsDevice.RasterizerState = RasterizerState.CullNone;

        _effect.View = Matrix.CreateLookAt(cameraTransform.Position, camera.Target, camera.Up);
        _effect.Projection = Matrix.CreatePerspectiveFieldOfView(
            camera.FieldOfView,
            graphicsDevice.Viewport.AspectRatio,
            camera.NearPlane,
            camera.FarPlane);

        foreach (var entity in world.Query<Transform3D, BoxCollider3D>())
        {
            var transform = world.Get<Transform3D>(entity);
            var collider = world.Get<BoxCollider3D>(entity);
            var worldMatrix = Matrix.CreateScale(collider.Size) *
                Matrix.CreateFromQuaternion(transform.Rotation) *
                Matrix.CreateTranslation(transform.Position);

            if (world.TryGet<PhysicsPose3D>(entity, out var physicsPose) && physicsPose.IsActive)
            {
                worldMatrix = physicsPose.CreateWorldMatrix(
                    collider.Size,
                    MathHelper.Clamp(context.InterpolationAlpha, 0f, 1f));
            }

            for (var index = 0; index < BoxCorners.Length; index++)
            {
                _vertices[index] = new VertexPositionColor(
                    Vector3.Transform(BoxCorners[index], worldMatrix),
                    Color);
            }

            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.LineList,
                    _vertices,
                    vertexOffset: 0,
                    numVertices: _vertices.Length,
                    BoxIndices,
                    indexOffset: 0,
                    primitiveCount: BoxIndices.Length / 2);
            }
        }
    }

    private static bool TryGetActiveCamera(
        World world,
        out Transform3D transform,
        out Camera3D camera)
    {
        foreach (var entity in world.Query<Transform3D, Camera3D>())
        {
            camera = world.Get<Camera3D>(entity);
            if (!camera.IsActive)
            {
                continue;
            }

            transform = world.Get<Transform3D>(entity);
            return true;
        }

        transform = default;
        camera = default;
        return false;
    }
}

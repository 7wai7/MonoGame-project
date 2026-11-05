using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Rendering;
using MonoGame_project.Engine.Rendering.ThreeD;
using MonoGame_project.Engine.Systems;

namespace MonoGame_project.Game.Terrain;

/// <summary>Draws terrain as world-aligned hexagons coloured by painted masks.</summary>
internal sealed class TerrainHoneycombRenderSystem : GameSystem, IContentSystem, IRenderSystem
{
    // A hexagon is 1 world units from vertex to opposite vertex.
    private const float HexRadius = 0.5f;
    private const float HexOutlineWidth = 0.01f;
    private const float VoronoiPatchSize = 10f;
    // 0.12 varies the base colour brightness by up to 6% in either direction.
    private const float VoronoiShadeStrength = 0.12f;

    private readonly Vector2 _worldOrigin;
    private readonly Vector2 _worldSize;
    private Effect? _effect;

    public TerrainHoneycombRenderSystem(Vector2 worldOrigin, Vector2 worldSize)
    {
        if (!float.IsFinite(worldOrigin.X) || !float.IsFinite(worldOrigin.Y) ||
            !float.IsFinite(worldSize.X) || !float.IsFinite(worldSize.Y) ||
            worldSize.X <= 0f || worldSize.Y <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(worldSize));
        }

        _worldOrigin = worldOrigin;
        _worldSize = worldSize;
    }

    public void LoadContent(World world, ContentManager content)
    {
        _effect = content.Load<Effect>("Effects/TerrainHoneycomb");
        var control = content.Load<Texture2D>("TerrainMaterials/Control");
        var river = content.Load<Texture2D>("TerrainMaterials/River");
        if (control.Width != river.Width || control.Height != river.Height)
        {
            throw new InvalidOperationException("Terrain control and river masks must have the same dimensions.");
        }

        _effect.Parameters["ControlTexture"].SetValue(control);
        _effect.Parameters["RiverTexture"].SetValue(river);
        _effect.Parameters["TerrainWorldOrigin"].SetValue(_worldOrigin);
        _effect.Parameters["TerrainWorldSize"].SetValue(_worldSize);
        _effect.Parameters["HexRadius"].SetValue(HexRadius);
        _effect.Parameters["HexOutlineWidth"].SetValue(HexOutlineWidth);
        _effect.Parameters["VoronoiPatchSize"].SetValue(VoronoiPatchSize);
        _effect.Parameters["VoronoiShadeStrength"].SetValue(VoronoiShadeStrength);
    }

    public void UnloadContent(World world)
    {
        // The scene's ContentManager owns the effect and both masks.
        _effect = null;
    }

    public void Draw(World world, in RenderContext context)
    {
        var statistics = world.GetResource<TerrainRenderStatistics>();
        if (statistics.IsEnabled)
        {
            statistics.Reset();
        }
        if (_effect is null ||
            !MeshRenderSystem.TryGetActiveCamera(world, out var cameraTransform, out var camera))
        {
            return;
        }

        MeshRenderSystem.ValidateCamera(cameraTransform, camera);
        var device = context.GraphicsDevice;
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        device.RasterizerState = RasterizerState.CullNone;
        device.SamplerStates[0] = SamplerState.LinearClamp;
        device.SamplerStates[1] = SamplerState.LinearClamp;

        var view = Matrix.CreateLookAt(cameraTransform.Position, camera.Target, camera.Up);
        var projection = Matrix.CreatePerspectiveFieldOfView(camera.FieldOfView,
            device.Viewport.AspectRatio, camera.NearPlane, camera.FarPlane);
        var frustum = new BoundingFrustum(view * projection);
        _effect.Parameters["View"].SetValue(view);
        _effect.Parameters["Projection"].SetValue(projection);

        foreach (var entity in world.Query<Transform3D, MeshRenderer, TerrainChunkTag>())
        {
            var renderer = world.Get<MeshRenderer>(entity);
            if (!renderer.IsVisible || renderer.UseBasicEffect ||
                renderer.Mesh is null || renderer.Mesh.IsDisposed)
            {
                continue;
            }

            var transform = world.Get<Transform3D>(entity);
            var worldMatrix = MeshRenderSystem.GetRenderWorldMatrix(
                world, entity, transform, context.InterpolationAlpha);
            if (MeshRenderSystem.IsOutsideFrustum(
                frustum, renderer.Mesh.SourceData.Bounds, worldMatrix, transform.Scale))
            {
                continue;
            }

            _effect.Parameters["World"].SetValue(worldMatrix);
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                renderer.Mesh.Draw(device);
                if (statistics.IsEnabled)
                {
                    statistics.Record(world.Get<TerrainRenderLodComponent>(entity).Value,
                        renderer.Mesh.PrimitiveCount);
                }
            }
        }
    }
}

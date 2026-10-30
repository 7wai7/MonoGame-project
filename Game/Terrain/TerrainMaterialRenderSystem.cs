using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Input;
using MonoGame_project.Engine.Rendering;
using MonoGame_project.Engine.Rendering.ThreeD;
using MonoGame_project.Engine.Systems;
using MonoGame_project.Engine.Time;

namespace MonoGame_project.Game.Terrain;

/// <summary>Draws streamed terrain meshes with world-space detail textures.</summary>
internal sealed class TerrainMaterialRenderSystem : GameSystem, IContentSystem, IUpdateSystem, IRenderSystem
{
    // These are the approximate real-world widths of the downloaded texture tiles.
    private const float GrassTileSize = 1f;
    private const float DirtTileSize = 1f;
    private const float RockTileSize = 0.9f;
    private const float SnowTileSize = 2f;
    // The displacement ranges are the 5th and 95th percentiles of each map.
    // They put unrelated source maps on a comparable scale at material edges.
    private static readonly Vector2 GrassHeightRange = new(0.38f, 0.643f);
    private static readonly Vector2 DirtHeightRange = new(0.285f, 0.526f);
    private static readonly Vector2 RockHeightRange = new(0.207f, 0.599f);
    private static readonly Vector2 SnowHeightRange = new(0.520f, 0.766f);
    private const float BoundaryHeightInfluence = 0.18f;
    private const float BoundaryNoiseInfluence = 0.10f;
    private const float BoundarySoftness = 0.025f;
    private const float DirtColorSoftness = 0.14f;
    private const float WorldColorVariationStrength = 0.18f;
    private const float DetailBlendStrength = 0.95f;
    private const float DetailFadeStart = 80f;
    private const float DetailFadeEnd = 650f;

    private Effect? _effect;
    private Texture2D? _control;
    private Texture2D? _river;
    private Texture2D? _grass;
    private Texture2D? _dirt;
    private Texture2D? _rock;
    private Texture2D? _snow;
    private Texture2D? _grassNormalRough;
    private Texture2D? _dirtNormalRough;
    private Texture2D? _rockNormalRough;
    private Texture2D? _snowNormalRough;
    private Texture2D? _grassHeight;
    private Texture2D? _dirtHeight;
    private Texture2D? _rockHeight;
    private Texture2D? _snowHeight;
    private MaterialDebugMode _debugMode;

    public void Update(World world, in FrameTime time)
    {
        if (!Context.Input.HasActiveInput ||
            !InputContexts.CanUseInspect(Context.InputContexts) ||
            !Context.Input.WasKeyPressed(Keys.F4))
        {
            return;
        }

        _debugMode = (MaterialDebugMode)(((int)_debugMode + 1) % 4);
        var legend = _debugMode switch
        {
            MaterialDebugMode.Materials =>
                "green=grass, orange=soil, red=rock, white=snow; final mask boundaries.",
            MaterialDebugMode.RockWeight =>
                "final rock weight: black=0, white=1; from the control mask.",
            MaterialDebugMode.Suppression =>
                "surface masks: black=ground, blue=river, white=snow.",
            _ => "normal terrain rendering."
        };
        Console.WriteLine($"[terrain-material] F4: {_debugMode}. {legend}");
    }

    public void LoadContent(World world, ContentManager content)
    {
        _effect = content.Load<Effect>("Effects/TerrainMaterial");
        _control = content.Load<Texture2D>("TerrainMaterials/Control");
        _river = content.Load<Texture2D>("TerrainMaterials/River");
        if (_control.Width != _river.Width || _control.Height != _river.Height)
        {
            throw new InvalidOperationException("Terrain control and river masks must have the same dimensions.");
        }
        _grass = content.Load<Texture2D>("TerrainMaterials/Grass");
        _dirt = content.Load<Texture2D>("TerrainMaterials/Dirt");
        _rock = content.Load<Texture2D>("TerrainMaterials/Rock");
        _snow = content.Load<Texture2D>("TerrainMaterials/Snow");
        _grassNormalRough = content.Load<Texture2D>("TerrainMaterials/GrassNormalRough");
        _dirtNormalRough = content.Load<Texture2D>("TerrainMaterials/DirtNormalRough");
        _rockNormalRough = content.Load<Texture2D>("TerrainMaterials/RockNormalRough");
        _snowNormalRough = content.Load<Texture2D>("TerrainMaterials/SnowNormalRough");
        _grassHeight = content.Load<Texture2D>("TerrainMaterials/GrassHeight");
        _dirtHeight = content.Load<Texture2D>("TerrainMaterials/DirtHeight");
        _rockHeight = content.Load<Texture2D>("TerrainMaterials/RockHeight");
        _snowHeight = content.Load<Texture2D>("TerrainMaterials/SnowHeight");

        _effect.Parameters["ControlTexture"].SetValue(_control);
        _effect.Parameters["RiverTexture"].SetValue(_river);
        _effect.Parameters["GrassTexture"].SetValue(_grass);
        _effect.Parameters["DirtTexture"].SetValue(_dirt);
        _effect.Parameters["RockTexture"].SetValue(_rock);
        _effect.Parameters["SnowTexture"].SetValue(_snow);
        _effect.Parameters["GrassNormalRoughTexture"].SetValue(_grassNormalRough);
        _effect.Parameters["DirtNormalRoughTexture"].SetValue(_dirtNormalRough);
        _effect.Parameters["RockNormalRoughTexture"].SetValue(_rockNormalRough);
        _effect.Parameters["SnowNormalRoughTexture"].SetValue(_snowNormalRough);
        _effect.Parameters["GrassHeightTexture"].SetValue(_grassHeight);
        _effect.Parameters["DirtHeightTexture"].SetValue(_dirtHeight);
        _effect.Parameters["RockHeightTexture"].SetValue(_rockHeight);
        _effect.Parameters["SnowHeightTexture"].SetValue(_snowHeight);
        _effect.Parameters["GrassTileSize"].SetValue(GrassTileSize);
        _effect.Parameters["DirtTileSize"].SetValue(DirtTileSize);
        _effect.Parameters["RockTileSize"].SetValue(RockTileSize);
        _effect.Parameters["SnowTileSize"].SetValue(SnowTileSize);
        _effect.Parameters["GrassHeightRange"].SetValue(GrassHeightRange);
        _effect.Parameters["DirtHeightRange"].SetValue(DirtHeightRange);
        _effect.Parameters["RockHeightRange"].SetValue(RockHeightRange);
        _effect.Parameters["SnowHeightRange"].SetValue(SnowHeightRange);
        _effect.Parameters["BoundaryHeightInfluence"].SetValue(BoundaryHeightInfluence);
        _effect.Parameters["BoundaryNoiseInfluence"].SetValue(BoundaryNoiseInfluence);
        _effect.Parameters["BoundarySoftness"].SetValue(BoundarySoftness);
        _effect.Parameters["DirtColorSoftness"].SetValue(DirtColorSoftness);
        _effect.Parameters["WorldColorVariationStrength"].SetValue(WorldColorVariationStrength);
        _effect.Parameters["DetailBlendStrength"].SetValue(DetailBlendStrength);
        _effect.Parameters["DetailFadeStart"].SetValue(DetailFadeStart);
        _effect.Parameters["DetailFadeEnd"].SetValue(DetailFadeEnd);
    }

    public void UnloadContent(World world)
    {
        // ContentManager owns these shared textures and the effect.
        _effect = null;
        _control = null;
        _river = null;
        _grass = null;
        _dirt = null;
        _rock = null;
        _snow = null;
        _grassNormalRough = null;
        _dirtNormalRough = null;
        _rockNormalRough = null;
        _snowNormalRough = null;
        _grassHeight = null;
        _dirtHeight = null;
        _rockHeight = null;
        _snowHeight = null;
    }

    public void Draw(World world, in RenderContext context)
    {
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
        for (var slot = 2; slot < 14; slot++)
        {
            device.SamplerStates[slot] = SamplerState.AnisotropicWrap;
        }

        var view = Matrix.CreateLookAt(cameraTransform.Position, camera.Target, camera.Up);
        var projection = Matrix.CreatePerspectiveFieldOfView(camera.FieldOfView,
            device.Viewport.AspectRatio, camera.NearPlane, camera.FarPlane);
        var frustum = new BoundingFrustum(view * projection);
        _effect.Parameters["View"].SetValue(view);
        _effect.Parameters["Projection"].SetValue(projection);
        _effect.Parameters["CameraPosition"].SetValue(cameraTransform.Position);
        _effect.Parameters["DebugMode"].SetValue((float)_debugMode);

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
            }
        }
    }

    private enum MaterialDebugMode
    {
        Normal,
        Materials,
        RockWeight,
        Suppression
    }
}

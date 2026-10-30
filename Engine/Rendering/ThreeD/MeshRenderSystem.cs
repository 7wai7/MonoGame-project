using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Physics.Components;
using MonoGame_project.Engine.Systems;

namespace MonoGame_project.Engine.Rendering.ThreeD;

/// <summary>Renders entities containing Transform3D and MeshRenderer components.</summary>
public sealed class MeshRenderSystem : GameSystem, IContentSystem, IRenderSystem
{
    private BasicEffect _effect = null!;
    private bool _contentLoaded;

    public void LoadContent(World world, ContentManager content)
    {
        _effect = new BasicEffect(Context.GraphicsDevice)
        {
            TextureEnabled = false,
            VertexColorEnabled = false,
            PreferPerPixelLighting = true,
            AmbientLightColor = new Vector3(0.22f)
        };
        _effect.EnableDefaultLighting();
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
        if (!_contentLoaded || !TryGetActiveCamera(world, out var cameraTransform, out var camera))
        {
            return;
        }

        ValidateCamera(cameraTransform, camera);

        var graphicsDevice = context.GraphicsDevice;
        graphicsDevice.BlendState = BlendState.Opaque;
        graphicsDevice.DepthStencilState = DepthStencilState.Default;
        graphicsDevice.RasterizerState = RasterizerState.CullNone;
        graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;

        var view = Matrix.CreateLookAt(cameraTransform.Position, camera.Target, camera.Up);
        var projection = Matrix.CreatePerspectiveFieldOfView(
            camera.FieldOfView,
            graphicsDevice.Viewport.AspectRatio,
            camera.NearPlane,
            camera.FarPlane);
        _effect.View = view;
        _effect.Projection = projection;
        var frustum = new BoundingFrustum(view * projection);

        foreach (var entity in world.Query<Transform3D, MeshRenderer>())
        {
            var transform = world.Get<Transform3D>(entity);
            var renderer = world.Get<MeshRenderer>(entity);
            if (!renderer.IsVisible || !renderer.UseBasicEffect ||
                renderer.Mesh is null || renderer.Mesh.IsDisposed)
            {
                continue;
            }

            var worldMatrix = GetRenderWorldMatrix(world, entity, transform, context.InterpolationAlpha);
            if (IsOutsideFrustum(frustum, renderer.Mesh.SourceData.Bounds, worldMatrix, transform.Scale))
            {
                continue;
            }

            _effect.World = worldMatrix;

            _effect.DiffuseColor = renderer.Tint.ToVector3();
            _effect.Alpha = renderer.Tint.A / 255f;
            _effect.TextureEnabled = renderer.Texture is not null;
            _effect.Texture = renderer.Texture;
            graphicsDevice.SamplerStates[0] = renderer.Texture is null
                ? SamplerState.LinearWrap
                : SamplerState.LinearClamp;

            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                renderer.Mesh.Draw(graphicsDevice);
            }
        }
    }

    internal static bool IsOutsideFrustum(
        BoundingFrustum frustum,
        in BoundingBox localBounds,
        in Matrix worldMatrix,
        in Vector3 scale)
    {
        var localCenter = (localBounds.Min + localBounds.Max) * 0.5f;
        var localRadius = Vector3.Distance(localCenter, localBounds.Max);
        var largestScale = MathF.Max(MathF.Abs(scale.X),
            MathF.Max(MathF.Abs(scale.Y), MathF.Abs(scale.Z)));
        var worldSphere = new BoundingSphere(
            Vector3.Transform(localCenter, worldMatrix),
            localRadius * largestScale);
        return frustum.Contains(worldSphere) == ContainmentType.Disjoint;
    }

    internal static Matrix GetRenderWorldMatrix(
        World world,
        Entity entity,
        in Transform3D transform,
        float interpolationAlpha)
    {
        if (world.TryGet<PhysicsPose3D>(entity, out var pose) && pose.IsActive)
        {
            return pose.CreateWorldMatrix(
                transform.Scale,
                MathHelper.Clamp(interpolationAlpha, 0f, 1f));
        }

        return transform.WorldMatrix;
    }

    internal static bool TryGetActiveCamera(
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

    internal static void ValidateCamera(in Transform3D transform, in Camera3D camera)
    {
        if (camera.FieldOfView <= 0f || camera.FieldOfView >= MathHelper.Pi)
        {
            throw new InvalidOperationException("Camera field of view must be between 0 and PI radians.");
        }

        if (camera.NearPlane <= 0f || camera.FarPlane <= camera.NearPlane)
        {
            throw new InvalidOperationException("Camera clipping planes are invalid.");
        }

        if (camera.Up.LengthSquared() == 0f)
        {
            throw new InvalidOperationException("Camera up vector cannot be zero.");
        }

        if (Vector3.DistanceSquared(transform.Position, camera.Target) == 0f)
        {
            throw new InvalidOperationException("Camera position and target cannot be equal.");
        }
    }
}

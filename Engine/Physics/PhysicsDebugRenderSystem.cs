using System;
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
    private const int CircleSegments = 24;
    private const int MaximumLineVertices = 512;

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

    private static readonly int[] BoxEdges =
    [
        0, 1, 1, 2, 2, 3, 3, 0,
        4, 5, 5, 6, 6, 7, 7, 4,
        0, 4, 1, 5, 2, 6, 3, 7
    ];

    private readonly VertexPositionColor[] _lineVertices =
        new VertexPositionColor[MaximumLineVertices];
    private BasicEffect _effect = null!;
    private int _lineVertexCount;
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

        PrepareGraphicsDevice(context, cameraTransform, camera);
        DrawBoxes(world, context);
        DrawSpheres(world, context);
        DrawCylinders(world, context);
        DrawCapsules(world, context);
        DrawCones(world, context);
    }

    // -------------------------------------------------------------------------
    // Per-collider query and wireframe construction
    // -------------------------------------------------------------------------

    private void DrawBoxes(World world, in RenderContext context)
    {
        foreach (var entity in world.Query<Transform3D, BoxCollider3D>())
        {
            var collider = world.Get<BoxCollider3D>(entity);
            var pose = GetPoseMatrix(world, entity, context.InterpolationAlpha);
            var geometry = Matrix.CreateScale(collider.Size) * pose;

            BeginLines();
            for (var edge = 0; edge < BoxEdges.Length; edge += 2)
            {
                AddLine(
                    BoxCorners[BoxEdges[edge]],
                    BoxCorners[BoxEdges[edge + 1]],
                    geometry);
            }

            SubmitLines(context.GraphicsDevice);
        }
    }

    private void DrawSpheres(World world, in RenderContext context)
    {
        foreach (var entity in world.Query<Transform3D, SphereCollider3D>())
        {
            var collider = world.Get<SphereCollider3D>(entity);
            var pose = GetPoseMatrix(world, entity, context.InterpolationAlpha);

            BeginLines();
            AddCircle(collider.Radius, 0f, CirclePlane.XY, pose);
            AddCircle(collider.Radius, 0f, CirclePlane.XZ, pose);
            AddCircle(collider.Radius, 0f, CirclePlane.ZY, pose);
            SubmitLines(context.GraphicsDevice);
        }
    }

    private void DrawCylinders(World world, in RenderContext context)
    {
        foreach (var entity in world.Query<Transform3D, CylinderCollider3D>())
        {
            var collider = world.Get<CylinderCollider3D>(entity);
            var pose = GetPoseMatrix(world, entity, context.InterpolationAlpha);
            var halfHeight = collider.Height * 0.5f;

            BeginLines();
            AddCircle(collider.Radius, halfHeight, CirclePlane.XZ, pose);
            AddCircle(collider.Radius, -halfHeight, CirclePlane.XZ, pose);
            AddAxialLines(collider.Radius, -halfHeight, halfHeight, pose);
            SubmitLines(context.GraphicsDevice);
        }
    }

    private void DrawCapsules(World world, in RenderContext context)
    {
        foreach (var entity in world.Query<Transform3D, CapsuleCollider3D>())
        {
            var collider = world.Get<CapsuleCollider3D>(entity);
            var pose = GetPoseMatrix(world, entity, context.InterpolationAlpha);
            var halfLength = collider.Length * 0.5f;

            BeginLines();
            AddCircle(collider.Radius, halfLength, CirclePlane.XZ, pose);
            AddCircle(collider.Radius, -halfLength, CirclePlane.XZ, pose);
            AddAxialLines(collider.Radius, -halfLength, halfLength, pose);
            AddHemisphereArc(collider.Radius, halfLength, true, false, pose);
            AddHemisphereArc(collider.Radius, halfLength, true, true, pose);
            AddHemisphereArc(collider.Radius, -halfLength, false, false, pose);
            AddHemisphereArc(collider.Radius, -halfLength, false, true, pose);
            SubmitLines(context.GraphicsDevice);
        }
    }

    private void DrawCones(World world, in RenderContext context)
    {
        foreach (var entity in world.Query<Transform3D, ConeCollider3D>())
        {
            var collider = world.Get<ConeCollider3D>(entity);
            var pose = GetPoseMatrix(world, entity, context.InterpolationAlpha);

            // Jitter places a cone's origin at its center of mass: one quarter
            // of its height above the base, rather than at its bounding-box center.
            var baseY = -collider.Height * 0.25f;
            var apexY = collider.Height * 0.75f;

            BeginLines();
            AddCircle(collider.Radius, baseY, CirclePlane.XZ, pose);
            for (var segment = 0; segment < CircleSegments; segment += 3)
            {
                var angle = MathF.Tau * segment / CircleSegments;
                var point = new Vector3(
                    collider.Radius * MathF.Cos(angle),
                    baseY,
                    collider.Radius * MathF.Sin(angle));
                AddLine(point, new Vector3(0f, apexY, 0f), pose);
            }

            SubmitLines(context.GraphicsDevice);
        }
    }

    // -------------------------------------------------------------------------
    // Reusable primitive builders
    // -------------------------------------------------------------------------

    private void AddCircle(float radius, float offset, CirclePlane plane, in Matrix matrix)
    {
        for (var segment = 0; segment < CircleSegments; segment++)
        {
            var firstAngle = MathF.Tau * segment / CircleSegments;
            var secondAngle = MathF.Tau * (segment + 1) / CircleSegments;
            AddLine(
                CreateCirclePoint(radius, offset, firstAngle, plane),
                CreateCirclePoint(radius, offset, secondAngle, plane),
                matrix);
        }
    }

    private void AddAxialLines(float radius, float bottomY, float topY, in Matrix matrix)
    {
        for (var segment = 0; segment < CircleSegments; segment += 3)
        {
            var angle = MathF.Tau * segment / CircleSegments;
            var x = radius * MathF.Cos(angle);
            var z = radius * MathF.Sin(angle);
            AddLine(new Vector3(x, bottomY, z), new Vector3(x, topY, z), matrix);
        }
    }

    private void AddHemisphereArc(
        float radius,
        float centerY,
        bool upper,
        bool useZAxis,
        in Matrix matrix)
    {
        var arcSegments = CircleSegments / 2;
        for (var segment = 0; segment < arcSegments; segment++)
        {
            var firstAngle = MathF.PI * segment / arcSegments;
            var secondAngle = MathF.PI * (segment + 1) / arcSegments;
            AddLine(
                CreateHemispherePoint(radius, centerY, firstAngle, upper, useZAxis),
                CreateHemispherePoint(radius, centerY, secondAngle, upper, useZAxis),
                matrix);
        }
    }

    private void AddLine(in Vector3 first, in Vector3 second, in Matrix matrix)
    {
        if (_lineVertexCount + 2 > _lineVertices.Length)
        {
            throw new InvalidOperationException("Physics debug line buffer is too small.");
        }

        _lineVertices[_lineVertexCount++] = new VertexPositionColor(
            Vector3.Transform(first, matrix),
            Color);
        _lineVertices[_lineVertexCount++] = new VertexPositionColor(
            Vector3.Transform(second, matrix),
            Color);
    }

    private static Vector3 CreateCirclePoint(
        float radius,
        float offset,
        float angle,
        CirclePlane plane)
    {
        var cosine = radius * MathF.Cos(angle);
        var sine = radius * MathF.Sin(angle);
        return plane switch
        {
            CirclePlane.XY => new Vector3(cosine, sine, offset),
            CirclePlane.XZ => new Vector3(cosine, offset, sine),
            CirclePlane.ZY => new Vector3(offset, sine, cosine),
            _ => throw new ArgumentOutOfRangeException(nameof(plane))
        };
    }

    private static Vector3 CreateHemispherePoint(
        float radius,
        float centerY,
        float angle,
        bool upper,
        bool useZAxis)
    {
        var horizontal = radius * MathF.Cos(angle);
        var vertical = radius * MathF.Sin(angle) * (upper ? 1f : -1f);
        return useZAxis
            ? new Vector3(0f, centerY + vertical, horizontal)
            : new Vector3(horizontal, centerY + vertical, 0f);
    }

    // -------------------------------------------------------------------------
    // Rendering state, interpolated pose, and camera lookup
    // -------------------------------------------------------------------------

    private void PrepareGraphicsDevice(
        in RenderContext context,
        in Transform3D cameraTransform,
        in Camera3D camera)
    {
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
    }

    private static Matrix GetPoseMatrix(
        World world,
        Entity entity,
        float interpolationAlpha)
    {
        if (world.TryGet<PhysicsPose3D>(entity, out var pose) && pose.IsActive)
        {
            return pose.CreateWorldMatrix(
                Vector3.One,
                MathHelper.Clamp(interpolationAlpha, 0f, 1f));
        }

        var transform = world.Get<Transform3D>(entity);
        return Matrix.CreateFromQuaternion(transform.Rotation) *
            Matrix.CreateTranslation(transform.Position);
    }

    private void BeginLines() => _lineVertexCount = 0;

    private void SubmitLines(GraphicsDevice graphicsDevice)
    {
        if (_lineVertexCount == 0)
        {
            return;
        }

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            graphicsDevice.DrawUserPrimitives(
                PrimitiveType.LineList,
                _lineVertices,
                vertexOffset: 0,
                primitiveCount: _lineVertexCount / 2);
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

    private enum CirclePlane
    {
        XY,
        XZ,
        ZY
    }
}

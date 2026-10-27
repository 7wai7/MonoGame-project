using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Input;
using MonoGame_project.Engine.Systems;
using MonoGame_project.Engine.Time;

namespace MonoGame_project.Engine.Rendering.ThreeD;

/// <summary>
/// Provides free-flight and selected-entity orbit control for an inspect camera.
/// </summary>
public sealed class InspectCameraControlSystem : GameSystem, IUpdateSystem, IRenderSystem
{
    private const float MinimumOrbitDistance = 0.5f;
    private const float MaximumOrbitDistance = 1_000f;
    private const float ZoomFactorPerWheelStep = 0.85f;
    private const float ShiftMoveSpeedMultiplier = 2f;

    private Entity _orbitCamera = Entity.None;
    private float _orbitDistance;
    private float _orbitYaw;
    private float _orbitPitch;
    private bool _orbitMouseReady;

    public void Update(World world, in FrameTime time)
    {
        UpdateCursorContext();

        if (!TryGetControlledCamera(world, out var entity))
        {
            _orbitMouseReady = false;
            return;
        }

        ref var transform = ref world.Get<Transform3D>(entity);
        ref var camera = ref world.Get<Camera3D>(entity);
        ref var controller = ref world.Get<InspectCameraController3D>(entity);
        var canUseInspect = InputContexts.CanUseInspect(Context.InputContexts);
        if (world.TryGetResource<InspectSessionState>(out var session))
        {
            if (!session.OrbitTarget.IsNone &&
                (entity != _orbitCamera || session.Selected != session.OrbitTarget ||
                 !TryGetOrbitFocus(world, session.OrbitTarget, 1f, out _)))
            {
                StopOrbit(session);
            }

            if (canUseInspect && Context.Input.WasKeyPressed(Keys.C))
            {
                if (!session.OrbitTarget.IsNone)
                {
                    StopOrbit(session);
                    return;
                }

                if (TryStartOrbit(world, session, entity, ref transform, ref camera, controller))
                {
                    return;
                }
            }

            if (!session.OrbitTarget.IsNone)
            {
                var hasOrbitMouseInput = canUseInspect &&
                    Context.InputContexts.Current == InputContexts.InspectViewport &&
                    Context.Input.IsRelativeMouseCaptured;
                if (hasOrbitMouseInput && _orbitMouseReady)
                {
                    RotateOrbit(Context.Input.MouseDelta, controller.MouseSensitivity,
                        controller.PitchLimit);
                    ZoomOrbit();
                }

                // Ignore a stale wheel delta on the frame capture resumes.
                _orbitMouseReady = hasOrbitMouseInput;

                if (TryGetOrbitFocus(world, session.OrbitTarget, 1f, out var focus))
                {
                    SetOrbitPose(ref transform, ref camera, focus);
                }

                return;
            }
        }

        _orbitCamera = Entity.None;
        _orbitMouseReady = false;
        if (!canUseInspect)
        {
            return;
        }

        UpdateFreeCamera(ref transform, ref camera, controller, time.DeltaSeconds);
    }

    public void Draw(World world, in RenderContext context)
    {
        // Fixed physics steps run after Update. Follow the same interpolated
        // pose that MeshRenderSystem will draw in this frame.
        if (!world.TryGetResource<InspectSessionState>(out var session) ||
            session.OrbitTarget.IsNone)
        {
            return;
        }

        if (!TryGetControlledCamera(world, out var cameraEntity) ||
            cameraEntity != _orbitCamera ||
            !TryGetOrbitFocus(world, session.OrbitTarget, context.InterpolationAlpha, out var focus))
        {
            StopOrbit(session);
            return;
        }

        ref var transform = ref world.Get<Transform3D>(cameraEntity);
        ref var camera = ref world.Get<Camera3D>(cameraEntity);
        SetOrbitPose(ref transform, ref camera, focus);
    }

    private void UpdateFreeCamera(ref Transform3D transform, ref Camera3D camera,
        in InspectCameraController3D controller, float deltaSeconds)
    {
        var mouseDelta = Context.InputContexts.Current == InputContexts.InspectViewport
            ? Context.Input.MouseDelta
            : Point.Zero;
        var forward = CalculateViewDirection(
            transform.Position,
            camera.Target,
            mouseDelta,
            controller.MouseSensitivity,
            controller.PitchLimit);

        var horizontalForward = new Vector3(forward.X, 0f, forward.Z);
        horizontalForward.Normalize();
        var right = Vector3.Cross(horizontalForward, Vector3.Up);

        var keyboard = Context.Input.CurrentKeyboard;
        var movement = ReadMovement(
            keyboard,
            horizontalForward,
            right);
        var moveSpeedMultiplier = keyboard.IsKeyDown(Keys.LeftShift) ||
            keyboard.IsKeyDown(Keys.RightShift)
            ? ShiftMoveSpeedMultiplier
            : 1f;
        var displacement = movement * controller.MoveSpeed * moveSpeedMultiplier * deltaSeconds;

        transform.Position += displacement;
        camera.Target = transform.Position + forward;
        camera.Up = Vector3.Up;
    }

    private bool TryStartOrbit(World world, InspectSessionState session, Entity cameraEntity,
        ref Transform3D transform, ref Camera3D camera, in InspectCameraController3D controller)
    {
        if (!TryGetOrbitFocus(world, session.Selected, 1f, out var focus))
        {
            return false;
        }

        var forward = focus - transform.Position;
        if (forward.LengthSquared() <= 1e-8f)
        {
            forward = camera.Target - transform.Position;
        }

        if (forward.LengthSquared() <= 1e-8f)
        {
            forward = Vector3.Forward;
        }

        forward.Normalize();
        _orbitYaw = MathF.Atan2(forward.X, -forward.Z);
        _orbitPitch = MathHelper.Clamp(
            MathF.Asin(MathHelper.Clamp(forward.Y, -1f, 1f)),
            -controller.PitchLimit,
            controller.PitchLimit);
        _orbitDistance = MathHelper.Clamp(
            controller.OrbitDistance,
            MinimumOrbitDistance,
            MaximumOrbitDistance);
        _orbitCamera = cameraEntity;
        _orbitMouseReady = false;
        session.OrbitTarget = session.Selected;
        SetOrbitPose(ref transform, ref camera, focus);
        return true;
    }

    private void RotateOrbit(Point mouseDelta, float sensitivity, float pitchLimit)
    {
        _orbitYaw += mouseDelta.X * sensitivity;
        _orbitPitch = MathHelper.Clamp(
            _orbitPitch - mouseDelta.Y * sensitivity,
            -pitchLimit,
            pitchLimit);
    }

    private void ZoomOrbit()
    {
        var wheelDelta = (long)Context.Input.CurrentMouse.ScrollWheelValue -
            Context.Input.PreviousMouse.ScrollWheelValue;
        if (wheelDelta == 0)
        {
            return;
        }

        var factor = MathF.Pow(ZoomFactorPerWheelStep, wheelDelta / 120f);
        _orbitDistance = MathHelper.Clamp(
            _orbitDistance * factor,
            MinimumOrbitDistance,
            MaximumOrbitDistance);
    }

    private void SetOrbitPose(ref Transform3D transform, ref Camera3D camera, Vector3 focus)
    {
        var forward = DirectionFromAngles(_orbitYaw, _orbitPitch);
        transform.Position = focus - forward * _orbitDistance;
        camera.Target = focus;
        camera.Up = Vector3.Up;
    }

    private void StopOrbit(InspectSessionState session)
    {
        session.OrbitTarget = Entity.None;
        _orbitCamera = Entity.None;
        _orbitMouseReady = false;
    }

    private static bool TryGetOrbitFocus(World world, Entity target,
        float interpolationAlpha, out Vector3 focus)
    {
        if (!world.IsAlive(target) ||
            !world.TryGet<Transform3D>(target, out var transform) ||
            !world.TryGet<MeshRenderer>(target, out var renderer) ||
            !renderer.IsVisible || renderer.Mesh is null || renderer.Mesh.IsDisposed)
        {
            focus = Vector3.Zero;
            return false;
        }

        // Mesh origins are not always central (terrain chunks start at a corner).
        var bounds = renderer.Mesh.SourceData.Bounds;
        var localCenter = (bounds.Min + bounds.Max) * 0.5f;
        var worldMatrix = MeshRenderSystem.GetRenderWorldMatrix(
            world, target, transform, interpolationAlpha);
        focus = Vector3.Transform(localCenter, worldMatrix);
        return true;
    }

    private void UpdateCursorContext()
    {
        if (!Context.Input.HasActiveInput)
        {
            return;
        }

        var leftControlDown = Context.Input.CurrentKeyboard.IsKeyDown(Keys.LeftControl);
        if (leftControlDown && Context.InputContexts.Current == InputContexts.InspectViewport)
        {
            Context.InputContexts.Push(InputContexts.InspectCursor);
        }
        else if (!leftControlDown && Context.InputContexts.Current == InputContexts.InspectCursor)
        {
            Context.InputContexts.Pop(InputContexts.InspectCursor);
        }
    }

    internal static bool TryGetControlledCamera(World world, out Entity entity)
    {
        foreach (var candidate in world.Query<Transform3D, Camera3D>())
        {
            var camera = world.Get<Camera3D>(candidate);
            if (!camera.IsActive)
            {
                continue;
            }

            if (world.TryGet<InspectCameraController3D>(candidate, out var controller) &&
                controller.IsEnabled)
            {
                entity = candidate;
                return true;
            }

            break;
        }

        entity = Entity.None;
        return false;
    }

    private static Vector3 CalculateViewDirection(
        Vector3 position,
        Vector3 target,
        Point mouseDelta,
        float sensitivity,
        float pitchLimit)
    {
        var direction = target - position;
        if (direction.LengthSquared() <= float.Epsilon)
        {
            throw new InvalidOperationException(
                "An inspect camera's position and target cannot be equal.");
        }

        direction.Normalize();
        var yaw = MathF.Atan2(direction.X, -direction.Z);
        var pitch = MathF.Asin(MathHelper.Clamp(direction.Y, -1f, 1f));

        yaw += mouseDelta.X * sensitivity;
        pitch -= mouseDelta.Y * sensitivity;
        pitch = MathHelper.Clamp(pitch, -pitchLimit, pitchLimit);

        return DirectionFromAngles(yaw, pitch);
    }

    private static Vector3 DirectionFromAngles(float yaw, float pitch)
    {
        var horizontalScale = MathF.Cos(pitch);
        return new Vector3(
            MathF.Sin(yaw) * horizontalScale,
            MathF.Sin(pitch),
            -MathF.Cos(yaw) * horizontalScale);
    }

    private static Vector3 ReadMovement(
        KeyboardState keyboard,
        Vector3 forward,
        Vector3 right)
    {
        var movement = Vector3.Zero;

        if (keyboard.IsKeyDown(Keys.W))
        {
            movement += forward;
        }

        if (keyboard.IsKeyDown(Keys.S))
        {
            movement -= forward;
        }

        if (keyboard.IsKeyDown(Keys.D))
        {
            movement += right;
        }

        if (keyboard.IsKeyDown(Keys.A))
        {
            movement -= right;
        }

        if (keyboard.IsKeyDown(Keys.E))
        {
            movement += Vector3.Up;
        }

        if (keyboard.IsKeyDown(Keys.Q))
        {
            movement += Vector3.Down;
        }

        if (movement.LengthSquared() > 0f)
        {
            movement.Normalize();
        }

        return movement;
    }
}

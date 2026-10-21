using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Input;
using MonoGame_project.Engine.Systems;
using MonoGame_project.Engine.Time;

namespace MonoGame_project.Engine.Rendering.ThreeD;

/// <summary>
/// Provides opt-in WASD, Q/E and relative-mouse control for inspecting a scene.
/// </summary>
public sealed class InspectCameraControlSystem : GameSystem, IUpdateSystem
{
    public void Update(World world, in FrameTime time)
    {
        UpdateCursorContext();

        if (!InputContexts.CanUseInspect(Context.InputContexts) ||
            !TryGetControlledCamera(world, out var entity))
        {
            return;
        }

        ref var transform = ref world.Get<Transform3D>(entity);
        ref var camera = ref world.Get<Camera3D>(entity);
        ref var controller = ref world.Get<InspectCameraController3D>(entity);

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

        var movement = ReadMovement(
            Context.Input.CurrentKeyboard,
            horizontalForward,
            right);
        var displacement = movement * controller.MoveSpeed * time.DeltaSeconds;

        transform.Position += displacement;
        camera.Target = transform.Position + forward;
        camera.Up = Vector3.Up;
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

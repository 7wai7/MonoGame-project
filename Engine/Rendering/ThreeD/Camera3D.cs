using Microsoft.Xna.Framework;
using MonoGame_project.Ecs;

namespace MonoGame_project.Engine.Rendering.ThreeD;

/// <summary>Perspective-camera settings. Its position comes from Transform3D.</summary>
public struct Camera3D : IComponent
{
    public Camera3D(Vector3 target)
    {
        Target = target;
        Up = Vector3.Up;
        FieldOfView = MathHelper.ToRadians(60f);
        NearPlane = 0.1f;
        FarPlane = 1_000f;
        IsActive = true;
    }

    public Vector3 Target;
    public Vector3 Up;
    public float FieldOfView;
    public float NearPlane;
    public float FarPlane;
    public bool IsActive;
}

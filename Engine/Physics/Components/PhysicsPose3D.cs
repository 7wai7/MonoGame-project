using Microsoft.Xna.Framework;
using MonoGame_project.Ecs;

namespace MonoGame_project.Engine.Physics.Components;

/// <summary>Engine-managed fixed-step poses used only for smooth rendering.</summary>
internal struct PhysicsPose3D : IComponent
{
    public PhysicsPose3D(Vector3 position, Quaternion rotation)
    {
        PreviousPosition = position;
        PreviousRotation = rotation;
        CurrentPosition = position;
        CurrentRotation = rotation;
        IsActive = true;
    }

    public Vector3 PreviousPosition;
    public Quaternion PreviousRotation;
    public Vector3 CurrentPosition;
    public Quaternion CurrentRotation;
    public bool IsActive;

    public readonly Matrix CreateWorldMatrix(Vector3 scale, float interpolationAlpha)
    {
        var position = Vector3.Lerp(PreviousPosition, CurrentPosition, interpolationAlpha);
        var rotation = Quaternion.Slerp(PreviousRotation, CurrentRotation, interpolationAlpha);

        return Matrix.CreateScale(scale) *
            Matrix.CreateFromQuaternion(rotation) *
            Matrix.CreateTranslation(position);
    }
}

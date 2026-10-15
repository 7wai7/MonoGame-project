using Microsoft.Xna.Framework;
using MonoGame_project.Ecs;

namespace MonoGame_project.Engine.Rendering.ThreeD;

/// <summary>
/// Position, rotation and render scale of an entity in 3D space.
/// For a physics entity, the physics system owns Position and Rotation;
/// Scale remains a rendering value and does not resize a collider automatically.
/// </summary>
public struct Transform3D : IComponent
{
    public Transform3D(Vector3 position)
        : this(position, Quaternion.Identity, Vector3.One)
    {
    }

    public Transform3D(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Position = position;
        Rotation = rotation;
        Scale = scale;
    }

    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;

    public readonly Matrix WorldMatrix =>
        Matrix.CreateScale(Scale) *
        Matrix.CreateFromQuaternion(Rotation) *
        Matrix.CreateTranslation(Position);

    public static Transform3D Identity => new(Vector3.Zero);
}

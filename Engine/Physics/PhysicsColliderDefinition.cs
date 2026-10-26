using Microsoft.Xna.Framework;
using MonoGame_project.Engine.Physics.Components;

namespace MonoGame_project.Engine.Physics;

internal enum PhysicsBodyChange
{
    None,
    Created,
    ShapeChanged
}

internal enum PhysicsColliderKind
{
    Box,
    Sphere,
    Cylinder,
    Capsule,
    Cone
}

/// <summary>Engine-only collider description that does not expose a physics library.</summary>
internal readonly record struct PhysicsColliderDefinition(
    PhysicsColliderKind Kind,
    Vector3 Size,
    float Radius,
    float Height,
    float Length)
{
    public static PhysicsColliderDefinition From(in BoxCollider3D collider) =>
        new(PhysicsColliderKind.Box, collider.Size, 0f, 0f, 0f);

    public static PhysicsColliderDefinition From(in SphereCollider3D collider) =>
        new(PhysicsColliderKind.Sphere, Vector3.Zero, collider.Radius, 0f, 0f);

    public static PhysicsColliderDefinition From(in CylinderCollider3D collider) =>
        new(PhysicsColliderKind.Cylinder, Vector3.Zero, collider.Radius, collider.Height, 0f);

    public static PhysicsColliderDefinition From(in CapsuleCollider3D collider) =>
        new(PhysicsColliderKind.Capsule, Vector3.Zero, collider.Radius, 0f, collider.Length);

    public static PhysicsColliderDefinition From(in ConeCollider3D collider) =>
        new(PhysicsColliderKind.Cone, Vector3.Zero, collider.Radius, collider.Height, 0f);
}

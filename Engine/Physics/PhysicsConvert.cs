using Jitter2.LinearMath;
using Microsoft.Xna.Framework;

namespace MonoGame_project.Engine.Physics;

/// <summary>Explicit conversions between MonoGame and Jitter math types.</summary>
internal static class PhysicsConvert
{
    public static JVector ToJitter(in Vector3 value) => new(value.X, value.Y, value.Z);

    public static Vector3 ToMonoGame(in JVector value) => new(value.X, value.Y, value.Z);

    public static JQuaternion ToJitter(in Quaternion value) =>
        new(value.X, value.Y, value.Z, value.W);

    public static Quaternion ToMonoGame(in JQuaternion value) =>
        new(value.X, value.Y, value.Z, value.W);
}

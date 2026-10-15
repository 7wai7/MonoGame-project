using System;
using Microsoft.Xna.Framework;

namespace MonoGame_project.Engine.Physics;

/// <summary>Scene-wide physics configuration.</summary>
public sealed record PhysicsSettings
{
    public static PhysicsSettings Default { get; } = new();

    public Vector3 Gravity { get; init; } = new(0f, -9.81f, 0f);
    public bool UseMultithreading { get; init; }

    internal void Validate()
    {
        if (!IsFinite(Gravity))
        {
            throw new InvalidOperationException("Physics gravity must contain only finite values.");
        }
    }

    private static bool IsFinite(in Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}

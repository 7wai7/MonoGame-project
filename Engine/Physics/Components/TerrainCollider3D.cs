using System;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Terrain;

namespace MonoGame_project.Engine.Physics.Components;

/// <summary>Static terrain collision sourced from heights, never from a render mesh.</summary>
public readonly struct TerrainCollider3D : IComponent
{
    public TerrainCollider3D(
        TerrainHeightField heights,
        float friction = 0.8f)
    {
        ArgumentNullException.ThrowIfNull(heights);
        Heights = heights;
        Friction = friction;
        Validate();
    }

    public TerrainHeightField Heights { get; }
    public float Friction { get; }

    public void Validate()
    {
        if (Heights is null)
        {
            throw new InvalidOperationException("Terrain collider needs height data.");
        }

        if (!float.IsFinite(Friction) || Friction < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(Friction));
        }

    }
}

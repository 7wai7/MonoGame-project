using System;
using Microsoft.Xna.Framework;
using MonoGame_project.Ecs;

namespace MonoGame_project.Engine.Rendering.ThreeD;

/// <summary>Connects an ECS entity to a mesh and its basic material properties.</summary>
public struct MeshRenderer : IComponent
{
    public MeshRenderer(Mesh mesh, Color tint)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        Mesh = mesh;
        Tint = tint;
        IsVisible = true;
    }

    public Mesh Mesh;
    public Color Tint;
    public bool IsVisible;
}

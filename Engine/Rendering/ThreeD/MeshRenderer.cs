using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame_project.Ecs;

namespace MonoGame_project.Engine.Rendering.ThreeD;

/// <summary>Connects an ECS entity to a mesh and its basic material properties.</summary>
public struct MeshRenderer : IComponent
{
    public MeshRenderer(Mesh mesh, Color tint, Texture2D? texture = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        Mesh = mesh;
        Tint = tint;
        Texture = texture;
        IsVisible = true;
        UseBasicEffect = true;
    }

    public Mesh Mesh;
    public Color Tint;
    /// <summary>The scene owns and disposes this texture; the component only references it.</summary>
    public Texture2D? Texture;
    public bool IsVisible;
    /// <summary>False when another render system draws this mesh with its own material.</summary>
    public bool UseBasicEffect;
}

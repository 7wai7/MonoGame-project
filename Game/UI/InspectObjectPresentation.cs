using System.Globalization;
using Microsoft.Xna.Framework;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Physics.Components;
using MonoGame_project.Engine.Rendering.ThreeD;
using MonoGame_project.Engine.Terrain;
using MonoGame_project.Game.Terrain;

namespace MonoGame_project.Game.UI;

/// <summary>Converts inspected ECS state into small UI-facing values.</summary>
internal static class InspectObjectPresentation
{
    public static string Label(World world, Entity entity)
    {
        if (world.TryGet<TerrainChunkTag>(entity, out var terrain))
        {
            return $"Terrain ({terrain.ChunkX}, {terrain.ChunkZ})";
        }

        return $"{ColliderLabel(world, entity)} #{entity.Id}";
    }

    public static InspectSummaryData Summary(World world, Entity entity,
        Vector3 cameraPosition, Transform3D transform)
    {
        var title = Label(world, entity);
        if (world.TryGet<TerrainChunkTag>(entity, out var terrain))
        {
            var lod = world.TryGet<TerrainRenderLodComponent>(entity, out var renderLod)
                ? renderLod.Value.ToString() : "not loaded";
            var collision = world.Has<TerrainCollider3D>(entity)
                ? "Heightfield collision  loaded" : "Heightfield collision  not loaded";
            return new InspectSummaryData(title, $"Render LOD  {lod}",
                $"Chunk  ({terrain.ChunkX}, {terrain.ChunkZ})", collision);
        }

        var distance = Vector3.Distance(cameraPosition, transform.Position)
            .ToString("0.###", CultureInfo.InvariantCulture);
        var position = $"Position  {Compact(transform.Position.X)}, " +
            $"{Compact(transform.Position.Y)}, {Compact(transform.Position.Z)} m";
        var state = world.TryGet<PhysicsBody3D>(entity, out var body)
            ? $"Physics  {body.MotionType} · {ColliderLabel(world, entity)}"
            : "No physics body";
        return new InspectSummaryData(title, $"Distance  {distance} m", position, state);
    }

    private static string ColliderLabel(World world, Entity entity) =>
        world.Has<SphereCollider3D>(entity) ? "Sphere" :
        world.Has<BoxCollider3D>(entity) ? "Box" :
        world.Has<CapsuleCollider3D>(entity) ? "Capsule" :
        world.Has<CylinderCollider3D>(entity) ? "Cylinder" :
        world.Has<ConeCollider3D>(entity) ? "Cone" : "Object";

    private static string Compact(float value) =>
        value.ToString("0.#", CultureInfo.InvariantCulture);
}

internal readonly record struct InspectSummaryData(string Title, string Primary,
    string Secondary, string State);

using MonoGame_project.Ecs;

namespace MonoGame_project.Game.Terrain;

/// <summary>Stable terrain identity, independent of streamed render and collision components.</summary>
internal readonly struct TerrainChunkTag(int chunkX, int chunkZ) : IComponent
{
    public int ChunkX { get; } = chunkX;
    public int ChunkZ { get; } = chunkZ;
}

using MonoGame_project.Ecs;

namespace MonoGame_project.Game.Terrain;

/// <summary>The mesh level currently installed on a terrain entity.</summary>
internal enum TerrainRenderLod { Near, Middle, Far }

internal struct TerrainRenderLodComponent : IComponent
{
    public TerrainRenderLodComponent(TerrainRenderLod value) => Value = value;

    public TerrainRenderLod Value;
}

/// <summary>Counts terrain draw calls and submitted triangles for the latest frame.</summary>
internal sealed class TerrainRenderStatistics
{
    public bool IsEnabled { get; set; }
    public int NearChunks { get; private set; }
    public int MiddleChunks { get; private set; }
    public int FarChunks { get; private set; }
    public long NearTriangles { get; private set; }
    public long MiddleTriangles { get; private set; }
    public long FarTriangles { get; private set; }

    public int TotalChunks => NearChunks + MiddleChunks + FarChunks;
    public long TotalTriangles => NearTriangles + MiddleTriangles + FarTriangles;

    public void Reset()
    {
        NearChunks = MiddleChunks = FarChunks = 0;
        NearTriangles = MiddleTriangles = FarTriangles = 0;
    }

    public void Record(TerrainRenderLod lod, int triangles)
    {
        switch (lod)
        {
            case TerrainRenderLod.Near:
                NearChunks++;
                NearTriangles += triangles;
                break;
            case TerrainRenderLod.Middle:
                MiddleChunks++;
                MiddleTriangles += triangles;
                break;
            case TerrainRenderLod.Far:
                FarChunks++;
                FarTriangles += triangles;
                break;
        }
    }

    public string ToLogString() =>
        $"terrain drawn {TotalChunks} chunks/{TotalTriangles:N0} triangles " +
        $"(near {NearChunks}/{NearTriangles:N0}, " +
        $"middle {MiddleChunks}/{MiddleTriangles:N0}, " +
        $"far {FarChunks}/{FarTriangles:N0})";
}

using System;

namespace MonoGame_project.Engine.Terrain;

/// <summary>
/// Immutable height samples for one square terrain chunk. A one-sample border
/// is retained so adjacent chunks calculate identical edge normals.
/// </summary>
public sealed class TerrainHeightField
{
    private readonly float[] _samples;
    private readonly int _sampleWidth;

    public TerrainHeightField(
        int chunkX,
        int chunkZ,
        int cellCount,
        float cellSize,
        Func<float, float, float> sampleHeight)
    {
        if (cellCount < 1 || cellCount > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(cellCount));
        }

        if (!float.IsFinite(cellSize) || cellSize <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        }

        ArgumentNullException.ThrowIfNull(sampleHeight);

        ChunkX = chunkX;
        ChunkZ = chunkZ;
        CellCount = cellCount;
        CellSize = cellSize;
        OriginX = WorldCoordinate((long)chunkX * cellCount, cellSize);
        OriginZ = WorldCoordinate((long)chunkZ * cellCount, cellSize);
        if (!float.IsFinite(OriginX) || !float.IsFinite(OriginZ))
        {
            throw new ArgumentOutOfRangeException(nameof(chunkX), "Chunk origin must be finite.");
        }

        _sampleWidth = cellCount + 3;
        _samples = new float[_sampleWidth * _sampleWidth];
        var minimumHeight = float.PositiveInfinity;
        var maximumHeight = float.NegativeInfinity;
        for (var z = -1; z <= cellCount + 1; z++)
        {
            for (var x = -1; x <= cellCount + 1; x++)
            {
                // Sample by global grid index, so the two sides of a seam use
                // exactly the same noise coordinates even for fractional sizes.
                var worldX = WorldCoordinate((long)chunkX * cellCount + x, cellSize);
                var worldZ = WorldCoordinate((long)chunkZ * cellCount + z, cellSize);
                if (!float.IsFinite(worldX) || !float.IsFinite(worldZ))
                {
                    throw new ArgumentOutOfRangeException(nameof(chunkX), "Chunk samples must have finite coordinates.");
                }

                var height = sampleHeight(worldX, worldZ);
                if (!float.IsFinite(height))
                {
                    throw new ArgumentException("Terrain heights must be finite.", nameof(sampleHeight));
                }

                _samples[(z + 1) * _sampleWidth + x + 1] = height;
                if (x >= 0 && x <= cellCount && z >= 0 && z <= cellCount)
                {
                    minimumHeight = MathF.Min(minimumHeight, height);
                    maximumHeight = MathF.Max(maximumHeight, height);
                }
            }
        }

        MinHeight = minimumHeight;
        MaxHeight = maximumHeight;
    }

    public int ChunkX { get; }
    public int ChunkZ { get; }
    public int CellCount { get; }
    public float CellSize { get; }
    public float OriginX { get; }
    public float OriginZ { get; }
    public float MinHeight { get; }
    public float MaxHeight { get; }

    public float GetHeight(int x, int z)
    {
        if ((uint)x > (uint)CellCount)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Vertex coordinates must be inside the chunk.");
        }

        if ((uint)z > (uint)CellCount)
        {
            throw new ArgumentOutOfRangeException(nameof(z), "Vertex coordinates must be inside the chunk.");
        }

        return SampleHeight(x, z);
    }

    internal float SampleHeight(int x, int z) =>
        _samples[(z + 1) * _sampleWidth + x + 1];

    internal float SampleInterpolatedHeight(float x, float z)
    {
        var x0 = (int)MathF.Floor(x);
        var z0 = (int)MathF.Floor(z);
        var x1 = Math.Min(x0 + 1, CellCount + 1);
        var z1 = Math.Min(z0 + 1, CellCount + 1);
        var tx = x - x0;
        var tz = z - z0;
        var near0 = SampleHeight(x0, z0);
        var far0 = SampleHeight(x0, z1);
        var near = near0 + (SampleHeight(x1, z0) - near0) * tx;
        var far = far0 + (SampleHeight(x1, z1) - far0) * tx;
        return near + (far - near) * tz;
    }

    private static float WorldCoordinate(long gridIndex, float cellSize) =>
        (float)(gridIndex * (double)cellSize);
}

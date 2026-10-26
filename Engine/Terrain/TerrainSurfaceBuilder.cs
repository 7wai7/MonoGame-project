using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MonoGame_project.Engine.Terrain;

/// <summary>Creates terrain triangulations from immutable height samples.</summary>
public static class TerrainSurfaceBuilder
{
    /// <summary>
    /// Simplifies nearly planar regions. At every point, the surface differs
    /// vertically from the full-resolution triangle mesh by no more than
    /// <paramref name="maxHeightError"/>. The outer chunk edge keeps every
    /// height sample, so independently generated neighbors have matching seams.
    /// Wire indices are generated only if debug drawing requests them later.
    /// A finite maxPatchCellSpan prevents an almost-flat large patch from being
    /// represented by only one interior fan vertex.
    /// </summary>
    public static TerrainSurface CreateAdaptive(
        TerrainHeightField heights,
        float maxHeightError,
        int maxPatchCellSpan = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(heights);
        if (!float.IsFinite(maxHeightError) || maxHeightError < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(maxHeightError));
        }

        if (maxPatchCellSpan < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPatchCellSpan));
        }

        // A zero-error surface is a regular grid, not an adaptive approximation.
        // The physics debug wireframe uses these same source height samples.
        if (maxHeightError == 0f)
        {
            var fullIndices = CreateIndices(heights.CellCount);
            return new TerrainSurface(CreatePositions(heights), fullIndices);
        }

        var size = heights.CellCount;
        var patches = new List<Patch>();
        Subdivide(heights, 0, 0, size, size, maxHeightError * 0.5f,
            maxPatchCellSpan, patches);

        var width = size + 1;
        var selected = new bool[width * width];
        foreach (var patch in patches)
        {
            selected[patch.Z0 * width + patch.X0] = true;
            selected[patch.Z0 * width + patch.X1] = true;
            selected[patch.Z1 * width + patch.X0] = true;
            selected[patch.Z1 * width + patch.X1] = true;
        }

        // A complete border is independent of how either chunk subdivides.
        for (var i = 0; i <= size; i++)
        {
            selected[i] = true;
            selected[size * width + i] = true;
            selected[i * width] = true;
            selected[i * width + size] = true;
        }

        var positions = new List<Vector3>();
        var gridIndices = new int[selected.Length];
        Array.Fill(gridIndices, -1);
        for (var z = 0; z <= size; z++)
        {
            for (var x = 0; x <= size; x++)
            {
                var gridIndex = z * width + x;
                if (!selected[gridIndex])
                {
                    continue;
                }

                gridIndices[gridIndex] = positions.Count;
                positions.Add(new Vector3(
                    x * heights.CellSize,
                    heights.GetHeight(x, z),
                    z * heights.CellSize));
            }
        }

        var indices = new List<int>();
        var perimeter = new List<int>();
        foreach (var patch in patches)
        {
            if (patch.IsCell)
            {
                var a = gridIndices[patch.Z0 * width + patch.X0];
                var b = gridIndices[patch.Z0 * width + patch.X1];
                var c = gridIndices[patch.Z1 * width + patch.X0];
                var d = gridIndices[patch.Z1 * width + patch.X1];
                indices.AddRange([a, c, b, b, c, d]);
                continue;
            }

            CollectPerimeter(patch, width, selected, gridIndices, perimeter);
            var centerX = (patch.X0 + patch.X1) * 0.5f;
            var centerZ = (patch.Z0 + patch.Z1) * 0.5f;
            var center = positions.Count;
            positions.Add(new Vector3(
                centerX * heights.CellSize,
                PlaneHeight(heights, patch, centerX, centerZ),
                centerZ * heights.CellSize));

            // Perimeter is counter-clockwise viewed from above. A fan uses
            // every shared edge point, including finer neighbors' corners.
            for (var i = 0; i < perimeter.Count; i++)
            {
                indices.Add(center);
                indices.Add(perimeter[i]);
                indices.Add(perimeter[(i + 1) % perimeter.Count]);
            }
        }

        return new TerrainSurface(positions.ToArray(), indices.ToArray());
    }

    private static void Subdivide(
        TerrainHeightField heights,
        int x0,
        int z0,
        int x1,
        int z1,
        float halfError,
        int maxPatchCellSpan,
        List<Patch> patches)
    {
        var patch = new Patch(x0, z0, x1, z1);
        if (patch.IsCell)
        {
            patches.Add(patch);
            return;
        }

        // A one-cell-wide fan cannot reduce the triangle count. Split these
        // strips into exact source cells instead.
        if (x1 - x0 > 1 && z1 - z0 > 1 &&
            x1 - x0 <= maxPatchCellSpan && z1 - z0 <= maxPatchCellSpan &&
            FitsPlane(heights, patch, halfError))
        {
            patches.Add(patch);
            return;
        }

        if (x1 - x0 > 1 && z1 - z0 > 1)
        {
            var midX = x0 + (x1 - x0) / 2;
            var midZ = z0 + (z1 - z0) / 2;
            Subdivide(heights, x0, z0, midX, midZ, halfError, maxPatchCellSpan, patches);
            Subdivide(heights, midX, z0, x1, midZ, halfError, maxPatchCellSpan, patches);
            Subdivide(heights, x0, midZ, midX, z1, halfError, maxPatchCellSpan, patches);
            Subdivide(heights, midX, midZ, x1, z1, halfError, maxPatchCellSpan, patches);
        }
        else if (x1 - x0 > 1)
        {
            var midX = x0 + (x1 - x0) / 2;
            Subdivide(heights, x0, z0, midX, z1, halfError, maxPatchCellSpan, patches);
            Subdivide(heights, midX, z0, x1, z1, halfError, maxPatchCellSpan, patches);
        }
        else
        {
            var midZ = z0 + (z1 - z0) / 2;
            Subdivide(heights, x0, z0, x1, midZ, halfError, maxPatchCellSpan, patches);
            Subdivide(heights, x0, midZ, x1, z1, halfError, maxPatchCellSpan, patches);
        }
    }

    private static bool FitsPlane(TerrainHeightField heights, Patch patch, float halfError)
    {
        var origin = heights.GetHeight(patch.X0, patch.Z0);
        var xSlope = (heights.GetHeight(patch.X1, patch.Z0) - origin) / (patch.X1 - patch.X0);
        var zSlope = (heights.GetHeight(patch.X0, patch.Z1) - origin) / (patch.Z1 - patch.Z0);
        for (var z = patch.Z0; z <= patch.Z1; z++)
        {
            for (var x = patch.X0; x <= patch.X1; x++)
            {
                var expected = origin + (x - patch.X0) * xSlope + (z - patch.Z0) * zSlope;
                var difference = MathF.Abs(heights.GetHeight(x, z) - expected);
                if (!float.IsFinite(difference) || difference > halfError)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static float PlaneHeight(TerrainHeightField heights, Patch patch, float x, float z)
    {
        var origin = heights.GetHeight(patch.X0, patch.Z0);
        var xSlope = (heights.GetHeight(patch.X1, patch.Z0) - origin) / (patch.X1 - patch.X0);
        var zSlope = (heights.GetHeight(patch.X0, patch.Z1) - origin) / (patch.Z1 - patch.Z0);
        return origin + (x - patch.X0) * xSlope + (z - patch.Z0) * zSlope;
    }

    private static void CollectPerimeter(
        Patch patch,
        int width,
        bool[] selected,
        int[] gridIndices,
        List<int> perimeter)
    {
        perimeter.Clear();
        for (var z = patch.Z0; z <= patch.Z1; z++)
        {
            AddIfSelected(patch.X0, z, width, selected, gridIndices, perimeter);
        }

        for (var x = patch.X0 + 1; x <= patch.X1; x++)
        {
            AddIfSelected(x, patch.Z1, width, selected, gridIndices, perimeter);
        }

        for (var z = patch.Z1 - 1; z >= patch.Z0; z--)
        {
            AddIfSelected(patch.X1, z, width, selected, gridIndices, perimeter);
        }

        for (var x = patch.X1 - 1; x > patch.X0; x--)
        {
            AddIfSelected(x, patch.Z0, width, selected, gridIndices, perimeter);
        }
    }

    private static void AddIfSelected(
        int x,
        int z,
        int width,
        bool[] selected,
        int[] gridIndices,
        List<int> perimeter)
    {
        var index = z * width + x;
        if (selected[index])
        {
            perimeter.Add(gridIndices[index]);
        }
    }

    internal static int[] CreateWireIndices(IReadOnlyList<int> triangleIndices)
    {
        var seen = new HashSet<ulong>();
        var lines = new List<int>();
        for (var i = 0; i < triangleIndices.Count; i += 3)
        {
            AddEdge(triangleIndices[i], triangleIndices[i + 1], seen, lines);
            AddEdge(triangleIndices[i + 1], triangleIndices[i + 2], seen, lines);
            AddEdge(triangleIndices[i + 2], triangleIndices[i], seen, lines);
        }

        return lines.ToArray();
    }

    private static void AddEdge(int first, int second, HashSet<ulong> seen, List<int> lines)
    {
        var low = Math.Min(first, second);
        var high = Math.Max(first, second);
        var key = ((ulong)(uint)low << 32) | (uint)high;
        if (seen.Add(key))
        {
            lines.Add(first);
            lines.Add(second);
        }
    }

    private readonly record struct Patch(int X0, int Z0, int X1, int Z1)
    {
        public bool IsCell => X1 - X0 == 1 && Z1 - Z0 == 1;
    }

    /// <summary>Full-resolution positions, when an explicit regular grid is needed.</summary>
    public static Vector3[] CreatePositions(TerrainHeightField heights)
    {
        ArgumentNullException.ThrowIfNull(heights);
        var width = heights.CellCount + 1;
        var positions = new Vector3[width * width];
        for (var z = 0; z < width; z++)
        {
            for (var x = 0; x < width; x++)
            {
                positions[z * width + x] = new Vector3(
                    x * heights.CellSize,
                    heights.GetHeight(x, z),
                    z * heights.CellSize);
            }
        }

        return positions;
    }

    /// <summary>Full-resolution triangle indices, matching CreatePositions.</summary>
    public static int[] CreateIndices(int cellCount)
    {
        if (cellCount < 1 || cellCount > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(cellCount));
        }

        var indices = new int[cellCount * cellCount * 6];
        var width = cellCount + 1;
        var next = 0;
        for (var z = 0; z < cellCount; z++)
        {
            for (var x = 0; x < cellCount; x++)
            {
                var a = z * width + x;
                var b = a + 1;
                var c = a + width;
                var d = c + 1;

                // Counter-clockwise as viewed from above (+Y). Adaptive
                // unit-cell patches use this same source-grid diagonal.
                indices[next++] = a;
                indices[next++] = c;
                indices[next++] = b;
                indices[next++] = b;
                indices[next++] = c;
                indices[next++] = d;
            }
        }

        return indices;
    }
}

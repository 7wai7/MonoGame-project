using Gum.Wireframe;
using MonoGame_project.Engine.Diagnostics;
using MonoGame_project.Game.Terrain;

namespace MonoGame_project.Game.UI;

/// <summary>Binds measurements to the unframed text list in Performance.gusx.</summary>
internal sealed class PerformanceStatsView
{
    private readonly GraphicalUiElement _root = GumHudProject.CreateScreen("Performance");
    private readonly GraphicalUiElement _group;
    private readonly HudTextPair _fps;
    private readonly HudTextPair _frame;
    private readonly HudTextPair _chunks;
    private readonly HudTextPair _nearTriangles;
    private readonly HudTextPair _middleTriangles;
    private readonly HudTextPair _farTriangles;
    private readonly HudTextPair _memory;
    private readonly HudTextPair _heap;

    public PerformanceStatsView()
    {
        _group = GumHudProject.Require(_root, "StatsGroup");
        _fps = Bind("Fps");
        _frame = Bind("Frame");
        _chunks = Bind("Chunks");
        _nearTriangles = Bind("NearTriangles");
        _middleTriangles = Bind("MiddleTriangles");
        _farTriangles = Bind("FarTriangles");
        _memory = Bind("Memory");
        _heap = Bind("Heap");
    }

    public GraphicalUiElement Visual => _root;

    public bool Visible
    {
        get => _group.Visible;
        set => _group.Visible = value;
    }

    public void Set(FrameRateSample sample, TerrainRenderStatistics terrain,
        ProcessMemorySnapshot memory)
    {
        _fps.Set($"FPS  {sample.FramesPerSecond:F1}");
        _frame.Set($"FRAME  {sample.AverageMilliseconds:F1} / {sample.MaximumMilliseconds:F1} ms");
        _chunks.Set($"CHUNKS  N {terrain.NearChunks}  M {terrain.MiddleChunks}  F {terrain.FarChunks}");
        _nearTriangles.Set($"TRI N  {terrain.NearTriangles:N0}");
        _middleTriangles.Set($"TRI M  {terrain.MiddleTriangles:N0}");
        _farTriangles.Set($"TRI F  {terrain.FarTriangles:N0}");
        _memory.Set($"RESIDENT  {memory.ResidentBytes / (1024d * 1024d):F0} MiB");
        _heap.Set($"HEAP  {memory.ManagedHeapBytes / (1024d * 1024d):F0} MiB");
    }

    private HudTextPair Bind(string name) => new(
        GumHudProject.Require(_root, name),
        GumHudProject.Require(_root, name + "Shadow"));

    private readonly record struct HudTextPair(
        GraphicalUiElement Text, GraphicalUiElement Shadow)
    {
        public void Set(string value)
        {
            GumHudProject.SetText(Shadow, value);
            GumHudProject.SetText(Text, value);
        }
    }
}

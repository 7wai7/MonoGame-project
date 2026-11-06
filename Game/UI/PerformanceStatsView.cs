using Gum.DataTypes;
using Gum.GueDeriving;
using Gum.Managers;
using Gum.Wireframe;
using Microsoft.Xna.Framework;
using MonoGame_project.Engine.Diagnostics;
using MonoGame_project.Game.Terrain;

namespace MonoGame_project.Game.UI;

/// <summary>Presentation of frame and terrain statistics; measurement stays in PerformanceScreen.</summary>
internal sealed class PerformanceStatsView
{
    private const float MaxTextWidth = 260f;
    private const float EdgeInset = 18f;
    private static readonly Color TextColor = new(211, 211, 211);

    private readonly ContainerRuntime _root = new();
    private readonly TextRuntime _fps;
    private readonly TextRuntime _frame;
    private readonly TextRuntime _chunks;
    private readonly TextRuntime _nearTriangles;
    private readonly TextRuntime _middleTriangles;
    private readonly TextRuntime _farTriangles;
    private readonly TextRuntime _memory;
    private readonly TextRuntime _heap;

    public PerformanceStatsView()
    {
        _root.Dock(Dock.Fill);
        _root.Visible = false;
        var textGroup = new ContainerRuntime
        {
            X = EdgeInset,
            Width = -2f * EdgeInset,
            WidthUnits = DimensionUnitType.RelativeToParent,
            MinWidth = 1f,
            MaxWidth = MaxTextWidth,
            Height = 0f,
            HeightUnits = DimensionUnitType.RelativeToChildren,
            ChildrenLayout = ChildrenLayout.TopToBottomStack
        };
        textGroup.Anchor(Anchor.CenterVertically);
        _root.Children.Add(textGroup);

        _fps = AddText(textGroup, "FPS  --");
        _frame = AddText(textGroup, "FRAME  --");
        _chunks = AddText(textGroup, "CHUNKS  N --  M --  F --");
        _nearTriangles = AddText(textGroup, "TRI N  --");
        _middleTriangles = AddText(textGroup, "TRI M  --");
        _farTriangles = AddText(textGroup, "TRI F  --");
        _memory = AddText(textGroup, "RESIDENT  --");
        _heap = AddText(textGroup, "HEAP  --");
    }

    public GraphicalUiElement Visual => _root;

    public bool Visible
    {
        get => _root.Visible;
        set => _root.Visible = value;
    }

    public void Set(FrameRateSample sample, TerrainRenderStatistics terrain,
        ProcessMemorySnapshot memory)
    {
        UiText.Set(_fps, $"FPS  {sample.FramesPerSecond:F1}");
        UiText.Set(_frame, $"FRAME  {sample.AverageMilliseconds:F1} / {sample.MaximumMilliseconds:F1} ms");
        UiText.Set(_chunks, $"CHUNKS  N {terrain.NearChunks}  M {terrain.MiddleChunks}  F {terrain.FarChunks}");
        UiText.Set(_nearTriangles, $"TRI N  {terrain.NearTriangles:N0}");
        UiText.Set(_middleTriangles, $"TRI M  {terrain.MiddleTriangles:N0}");
        UiText.Set(_farTriangles, $"TRI F  {terrain.FarTriangles:N0}");
        UiText.Set(_memory, $"RESIDENT  {memory.ResidentBytes / (1024d * 1024d):F0} MiB");
        UiText.Set(_heap, $"HEAP  {memory.ManagedHeapBytes / (1024d * 1024d):F0} MiB");
    }

    private static TextRuntime AddText(ContainerRuntime parent, string value)
    {
        var text = UiText.Create(11f, TextColor, 18f);
        text.Text = value;
        parent.Children.Add(text);
        return text;
    }
}

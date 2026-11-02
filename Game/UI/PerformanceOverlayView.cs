using Gum.GueDeriving;
using Gum.Managers;
using Gum.Wireframe;
using Microsoft.Xna.Framework;
using MonoGame_project.Engine.Diagnostics;
using MonoGame_project.Game.Terrain;

namespace MonoGame_project.Game.UI;

/// <summary>Small, noninteractive performance text beside the viewport.</summary>
internal sealed class PerformanceOverlayView : IDisposable
{
    private const float TextWidth = 260f;
    private const float LeftMargin = 18f;
    private static readonly Color TextColor = new(211, 211, 211);

    private readonly ContainerRuntime _root = new();
    private readonly ContainerRuntime _textGroup;
    private readonly TextRuntime _fps;
    private readonly TextRuntime _frame;
    private readonly TextRuntime _chunks;
    private readonly TextRuntime _nearTriangles;
    private readonly TextRuntime _middleTriangles;
    private readonly TextRuntime _farTriangles;
    private readonly TextRuntime _memory;
    private readonly TextRuntime _heap;
    private int _viewportWidth;
    private bool _disposed;

    public PerformanceOverlayView()
    {
        _root.Dock(Dock.Fill);
        _textGroup = new ContainerRuntime { Width = TextWidth, Height = 112f };
        _textGroup.Anchor(Anchor.Center);
        _root.Children.Add(_textGroup);

        _fps = AddText("FPS  --", 0f);
        _frame = AddText("FRAME  --", 14f);
        _chunks = AddText("CHUNKS  N --  M --  F --", 28f);
        _nearTriangles = AddText("TRI N  --", 42f);
        _middleTriangles = AddText("TRI M  --", 56f);
        _farTriangles = AddText("TRI F  --", 70f);
        _memory = AddText("RESIDENT  --", 84f);
        _heap = AddText("HEAP  --", 98f);

        _root.Visible = false;
        _root.AddToRoot();
    }

    public bool IsVisible
    {
        get => _root.Visible;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _root.Visible = value;
        }
    }

    public void SetViewportWidth(int width)
    {
        if (_viewportWidth == width)
        {
            return;
        }

        _viewportWidth = width;
        _textGroup.X = -width * 0.5f + TextWidth * 0.5f + LeftMargin;
    }

    public void SetMetrics(double fps, double averageMs, double maximumMs,
        ProcessMemorySnapshot memory, TerrainRenderStatistics terrain)
    {
        SetText(_fps, $"FPS  {fps:F1}");
        SetText(_frame, $"FRAME  {averageMs:F1} / {maximumMs:F1} ms");
        SetText(_chunks,
            $"CHUNKS  N {terrain.NearChunks}  M {terrain.MiddleChunks}  F {terrain.FarChunks}");
        SetText(_nearTriangles, $"TRI N  {terrain.NearTriangles:N0}");
        SetText(_middleTriangles, $"TRI M  {terrain.MiddleTriangles:N0}");
        SetText(_farTriangles, $"TRI F  {terrain.FarTriangles:N0}");
        SetText(_memory, $"RESIDENT  {memory.ResidentBytes / (1024d * 1024d):F0} MiB");
        SetText(_heap, $"HEAP  {memory.ManagedHeapBytes / (1024d * 1024d):F0} MiB");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _root.RemoveFromRoot();
        _disposed = true;
    }

    private TextRuntime AddText(string value, float y)
    {
        var text = new TextRuntime
        {
            Text = value,
            Y = y,
            Width = TextWidth,
            Height = 18f,
            FontSize = 11f,
            UseFontSmoothing = true,
            Color = TextColor
        };
        _textGroup.Children.Add(text);
        return text;
    }

    private static void SetText(TextRuntime target, string value)
    {
        if (target.Text != value)
        {
            target.Text = value;
        }
    }
}

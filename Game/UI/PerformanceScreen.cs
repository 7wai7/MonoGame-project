using Gum.GueDeriving;
using Gum.Managers;
using Gum.Wireframe;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoGame_project.Ecs;
using MonoGame_project.Engine;
using MonoGame_project.Engine.Diagnostics;
using MonoGame_project.Engine.UI;
using MonoGame_project.Game.Terrain;

namespace MonoGame_project.Game.UI;

/// <summary>Owns F11 statistics, their text controls and terminal logging.</summary>
internal sealed class PerformanceScreen : UiScreen
{
    private const float TextWidth = 260f;
    private const float LeftMargin = 18f;
    private static readonly Color TextColor = new(211, 211, 211);

    private readonly EngineContext _context;
    private readonly World _world;
    private readonly FrameRateMeter _meter = new();
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

    public PerformanceScreen(EngineContext context, World world)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _root.Dock(Dock.Fill);
        _root.Visible = false;
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
    }

    public override GraphicalUiElement Visual => _root;

    public override void HandleInput()
    {
        if (!_context.Input.HasActiveInput || !_context.Input.WasKeyPressed(Keys.F11))
        {
            return;
        }

        _root.Visible = !_root.Visible;
        _world.GetResource<TerrainRenderStatistics>().IsEnabled = _root.Visible;
        _meter.Reset();
        Console.WriteLine($"[performance] F11: {(_root.Visible ? "on" : "off")}. " +
            "FPS, frame time and memory are logged every five seconds while visible.");
    }

    public override void Refresh()
    {
        var width = _context.GraphicsDevice.Viewport.Width;
        if (_viewportWidth != width)
        {
            _viewportWidth = width;
            _textGroup.X = -width * 0.5f + TextWidth * 0.5f + LeftMargin;
        }
    }

    public override void AfterSceneDraw()
    {
        if (!_root.Visible)
        {
            return;
        }

        _meter.Record(out var display, out var log);
        var terrain = _world.GetResource<TerrainRenderStatistics>();
        if (display is { } sample)
        {
            var memory = ProcessMemorySnapshot.Capture();
            _fps.Text = $"FPS  {sample.FramesPerSecond:F1}";
            _frame.Text = $"FRAME  {sample.AverageMilliseconds:F1} / {sample.MaximumMilliseconds:F1} ms";
            _chunks.Text = $"CHUNKS  N {terrain.NearChunks}  M {terrain.MiddleChunks}  F {terrain.FarChunks}";
            _nearTriangles.Text = $"TRI N  {terrain.NearTriangles:N0}";
            _middleTriangles.Text = $"TRI M  {terrain.MiddleTriangles:N0}";
            _farTriangles.Text = $"TRI F  {terrain.FarTriangles:N0}";
            _memory.Text = $"RESIDENT  {memory.ResidentBytes / (1024d * 1024d):F0} MiB";
            _heap.Text = $"HEAP  {memory.ManagedHeapBytes / (1024d * 1024d):F0} MiB";
        }

        if (log is { } logSample)
        {
            Console.WriteLine(
                $"[performance] {logSample.DurationSeconds:F1}s / {logSample.FrameCount} frames: " +
                $"{logSample.FramesPerSecond:F1} FPS, " +
                $"frame avg {logSample.AverageMilliseconds:F2}ms, " +
                $"max {logSample.MaximumMilliseconds:F2}ms; " +
                $"{ProcessMemorySnapshot.Capture()}; {terrain.ToLogString()}.");
        }
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
}

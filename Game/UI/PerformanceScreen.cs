using System.Diagnostics;
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
    private string? _captureLabel;
    private long _lastCaptureDrawAt;
    private int _captureFrames;
    private double _captureTotalMilliseconds;
    private double _captureMaximumMilliseconds;

    public PerformanceScreen(EngineContext context, World world)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _root.Dock(Dock.Fill);
        _root.Visible = false;
        _textGroup = new ContainerRuntime
        {
            Width = TextWidth,
            Height = 144f,
            ChildrenLayout = ChildrenLayout.TopToBottomStack
        };
        _textGroup.Anchor(Anchor.CenterVertically);
        _textGroup.X = LeftMargin;
        _root.Children.Add(_textGroup);

        _fps = AddText("FPS  --");
        _frame = AddText("FRAME  --");
        _chunks = AddText("CHUNKS  N --  M --  F --");
        _nearTriangles = AddText("TRI N  --");
        _middleTriangles = AddText("TRI M  --");
        _farTriangles = AddText("TRI F  --");
        _memory = AddText("RESIDENT  --");
        _heap = AddText("HEAP  --");
    }

    public override GraphicalUiElement Visual => _root;

    public override void HandleInput()
    {
        if (_captureLabel is not null || !_context.Input.HasActiveInput ||
            !_context.Input.WasKeyPressed(Keys.F11))
        {
            return;
        }

        _root.Visible = !_root.Visible;
        _world.GetResource<TerrainRenderStatistics>().IsEnabled = _root.Visible;
        _meter.Reset();
        Console.WriteLine($"[performance] F11: {(_root.Visible ? "on" : "off")}. " +
            "FPS, frame time and memory are logged every five seconds while visible.");
    }

    public override void AfterSceneDraw()
    {
        if (!_root.Visible)
        {
            return;
        }

        _meter.Record(out var display, out var log);
        if (_captureLabel is not null)
        {
            RecordCaptureFrame();
        }
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

    /// <summary>Starts a fixed-view benchmark using the same UI as F11.</summary>
    public void BeginCapture(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (_captureLabel is not null)
        {
            throw new InvalidOperationException("A performance capture is already running.");
        }

        _captureLabel = label;
        _captureFrames = 0;
        _captureTotalMilliseconds = 0d;
        _captureMaximumMilliseconds = 0d;
        _lastCaptureDrawAt = 0;
        _root.Visible = true;
        _world.GetResource<TerrainRenderStatistics>().IsEnabled = true;
        _meter.Reset();
        Console.WriteLine($"[benchmark] START {label}; F11 statistics reset.");
    }

    public void EndCapture()
    {
        if (_captureLabel is null)
        {
            throw new InvalidOperationException("No performance capture is running.");
        }

        var average = _captureFrames == 0
            ? 0d : _captureTotalMilliseconds / _captureFrames;
        var fps = average > 0d ? 1_000d / average : 0d;
        Console.WriteLine(
            $"[benchmark] END {_captureLabel}: {_captureFrames} frames, " +
            $"{fps:F1} FPS, frame avg {average:F2}ms, " +
            $"max {_captureMaximumMilliseconds:F2}ms; " +
            $"{_world.GetResource<TerrainRenderStatistics>().ToLogString()}.");
        StopCapture();
    }

    public void CancelCapture()
    {
        if (_captureLabel is null)
        {
            return;
        }

        Console.WriteLine($"[benchmark] CANCEL {_captureLabel}; partial frames discarded.");
        StopCapture();
    }

    private void RecordCaptureFrame()
    {
        var now = Stopwatch.GetTimestamp();
        if (_lastCaptureDrawAt != 0)
        {
            var frameMs = Stopwatch.GetElapsedTime(_lastCaptureDrawAt, now).TotalMilliseconds;
            _captureFrames++;
            _captureTotalMilliseconds += frameMs;
            _captureMaximumMilliseconds = Math.Max(_captureMaximumMilliseconds, frameMs);
        }

        _lastCaptureDrawAt = now;
    }

    private void StopCapture()
    {
        _captureLabel = null;
        _root.Visible = false;
        _world.GetResource<TerrainRenderStatistics>().IsEnabled = false;
        _lastCaptureDrawAt = 0;
        _meter.Reset();
    }

    private TextRuntime AddText(string value)
    {
        var text = new TextRuntime
        {
            Text = value,
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

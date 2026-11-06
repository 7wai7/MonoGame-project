using System.Diagnostics;
using Gum.Wireframe;
using Microsoft.Xna.Framework.Input;
using MonoGame_project.Ecs;
using MonoGame_project.Engine;
using MonoGame_project.Engine.Diagnostics;
using MonoGame_project.Engine.UI;
using MonoGame_project.Game.Terrain;

namespace MonoGame_project.Game.UI;

/// <summary>Owns F11 measurements, benchmark capture and terminal logging.</summary>
internal sealed class PerformanceScreen : UiScreen
{
    private readonly EngineContext _context;
    private readonly World _world;
    private readonly FrameRateMeter _meter = new();
    private readonly PerformanceStatsView _view;
    private string? _captureLabel;
    private long _lastCaptureDrawAt;
    private int _captureFrames;
    private double _captureTotalMilliseconds;
    private double _captureMaximumMilliseconds;

    public PerformanceScreen(EngineContext context, World world, GameUiTheme theme)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _view = new PerformanceStatsView(theme);
    }

    public override GraphicalUiElement Visual => _view.Visual;

    public override void HandleInput()
    {
        if (_captureLabel is not null || !_context.Input.HasActiveInput ||
            !_context.Input.WasKeyPressed(Keys.F11))
        {
            return;
        }

        _view.Visible = !_view.Visible;
        _world.GetResource<TerrainRenderStatistics>().IsEnabled = _view.Visible;
        _meter.Reset();
        Console.WriteLine($"[performance] F11: {(_view.Visible ? "on" : "off")}. " +
            "FPS, frame time and memory are logged every five seconds while visible.");
    }

    public override void AfterSceneDraw()
    {
        if (!_view.Visible)
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
            _view.Set(sample, terrain, ProcessMemorySnapshot.Capture());
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
        _view.Visible = true;
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
        _view.Visible = false;
        _world.GetResource<TerrainRenderStatistics>().IsEnabled = false;
        _lastCaptureDrawAt = 0;
        _meter.Reset();
    }

}

using System.Diagnostics;
using Microsoft.Xna.Framework.Input;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Diagnostics;
using MonoGame_project.Engine.Rendering;
using MonoGame_project.Engine.Systems;
using MonoGame_project.Engine.Time;
using MonoGame_project.Game.Terrain;

namespace MonoGame_project.Game.UI;

/// <summary>Measures real draw-to-draw time and shows/logs rolling statistics.</summary>
internal sealed class PerformanceOverlaySystem : GameSystem, IUpdateSystem, IRenderSystem
{
    private const double DisplayWindowSeconds = 0.5d;
    private const double LogWindowSeconds = 5d;

    private PerformanceOverlayView _view = null!;
    private FrameWindow _displayWindow;
    private FrameWindow _logWindow;
    private long _lastDrawAt;

    protected override void OnInitialize()
    {
        _view = new PerformanceOverlayView();
    }

    public void Update(World world, in FrameTime time)
    {
        if (!Context.Input.HasActiveInput ||
            !Context.Input.WasKeyPressed(Keys.F11))
        {
            return;
        }

        _view.IsVisible = !_view.IsVisible;
        world.GetResource<TerrainRenderStatistics>().IsEnabled = _view.IsVisible;
        _lastDrawAt = 0;
        _displayWindow = default;
        _logWindow = default;
        Console.WriteLine($"[performance] F11: {(_view.IsVisible ? "on" : "off")}. " +
            "FPS, frame time and memory are logged every five seconds while visible.");
    }

    public void Draw(World world, in RenderContext context)
    {
        if (!_view.IsVisible)
        {
            return;
        }

        _view.SetViewportWidth(context.GraphicsDevice.Viewport.Width);
        var now = Stopwatch.GetTimestamp();
        if (_lastDrawAt == 0)
        {
            _lastDrawAt = now;
            _displayWindow.Reset(now);
            _logWindow.Reset(now);
            return;
        }

        var frameMs = Stopwatch.GetElapsedTime(_lastDrawAt, now).TotalMilliseconds;
        _lastDrawAt = now;
        _displayWindow.Record(frameMs);
        _logWindow.Record(frameMs);
        var displaySeconds = _displayWindow.ElapsedSeconds(now);
        if (displaySeconds >= DisplayWindowSeconds)
        {
            var memory = ProcessMemorySnapshot.Capture();
            _view.SetMetrics(
                _displayWindow.Count / displaySeconds,
                _displayWindow.AverageMilliseconds,
                _displayWindow.MaximumMilliseconds,
                memory,
                world.GetResource<TerrainRenderStatistics>());
            _displayWindow.Reset(now);
        }

        var logSeconds = _logWindow.ElapsedSeconds(now);
        if (logSeconds >= LogWindowSeconds)
        {
            Console.WriteLine(
                $"[performance] {logSeconds:F1}s / {_logWindow.Count} frames: " +
                $"{_logWindow.Count / logSeconds:F1} FPS, " +
                $"frame avg {_logWindow.AverageMilliseconds:F2}ms, " +
                $"max {_logWindow.MaximumMilliseconds:F2}ms; " +
                $"{ProcessMemorySnapshot.Capture()}; " +
                $"{world.GetResource<TerrainRenderStatistics>().ToLogString()}.");
            _logWindow.Reset(now);
        }
    }

    protected override void OnShutdown()
    {
        _view.Dispose();
        _view = null!;
    }

    private struct FrameWindow
    {
        private long _startedAt;
        private double _totalMilliseconds;

        public int Count { get; private set; }
        public double MaximumMilliseconds { get; private set; }
        public double AverageMilliseconds => Count == 0 ? 0d : _totalMilliseconds / Count;

        public void Record(double frameMs)
        {
            Count++;
            _totalMilliseconds += frameMs;
            MaximumMilliseconds = Math.Max(MaximumMilliseconds, frameMs);
        }

        public double ElapsedSeconds(long now) =>
            Stopwatch.GetElapsedTime(_startedAt, now).TotalSeconds;

        public void Reset(long now)
        {
            _startedAt = now;
            _totalMilliseconds = 0d;
            Count = 0;
            MaximumMilliseconds = 0d;
        }
    }
}

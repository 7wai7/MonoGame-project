using System.Diagnostics;

namespace MonoGame_project.Engine.Diagnostics;

/// <summary>Measures draw-to-draw intervals for display and terminal windows.</summary>
public sealed class FrameRateMeter
{
    private const double DisplayWindowSeconds = 0.5d;
    private const double LogWindowSeconds = 5d;

    private FrameWindow _displayWindow;
    private FrameWindow _logWindow;
    private long _lastDrawAt;

    public void Reset()
    {
        _lastDrawAt = 0;
        _displayWindow = default;
        _logWindow = default;
    }

    public void Record(out FrameRateSample? display, out FrameRateSample? log)
    {
        display = null;
        log = null;
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
            display = _displayWindow.Snapshot(displaySeconds);
            _displayWindow.Reset(now);
        }

        var logSeconds = _logWindow.ElapsedSeconds(now);
        if (logSeconds >= LogWindowSeconds)
        {
            log = _logWindow.Snapshot(logSeconds);
            _logWindow.Reset(now);
        }
    }

    private struct FrameWindow
    {
        private long _startedAt;
        private double _totalMilliseconds;
        private int _count;
        private double _maximumMilliseconds;

        public void Record(double frameMs)
        {
            _count++;
            _totalMilliseconds += frameMs;
            _maximumMilliseconds = Math.Max(_maximumMilliseconds, frameMs);
        }

        public double ElapsedSeconds(long now) =>
            Stopwatch.GetElapsedTime(_startedAt, now).TotalSeconds;

        public FrameRateSample Snapshot(double seconds) => new(
            seconds,
            _count,
            _count / seconds,
            _count == 0 ? 0d : _totalMilliseconds / _count,
            _maximumMilliseconds);

        public void Reset(long now)
        {
            _startedAt = now;
            _totalMilliseconds = 0d;
            _count = 0;
            _maximumMilliseconds = 0d;
        }
    }
}

public readonly record struct FrameRateSample(
    double DurationSeconds,
    int FrameCount,
    double FramesPerSecond,
    double AverageMilliseconds,
    double MaximumMilliseconds);

using System.Diagnostics;

namespace McInstall.Core;

public sealed record ProgressSnapshot(
    string Phase,
    long DoneBytes,
    long TotalBytes,
    int DoneFiles,
    int TotalFiles,
    double BytesPerSecond,
    string? CurrentFile)
{
    public double Fraction => TotalBytes > 0 ? Math.Clamp((double)DoneBytes / TotalBytes, 0, 1) : 0;
    public TimeSpan? Eta => BytesPerSecond > 1 && TotalBytes > DoneBytes
        ? TimeSpan.FromSeconds((TotalBytes - DoneBytes) / BytesPerSecond)
        : null;
}

/// <summary>线程安全的进度聚合器。</summary>
public sealed class ProgressTracker
{
    private readonly object _lock = new();
    private long _totalBytes;
    private long _doneBytes;
    private int _totalFiles;
    private int _doneFiles;
    private string _phase = "";
    private string? _current;
    private double _speed;
    private long _lastSampleBytes;
    private long _lastSampleTimestamp = Stopwatch.GetTimestamp();

    public void SetPhase(string phase)
    {
        lock (_lock) _phase = phase;
    }

    public void SetTotals(int files, long bytes)
    {
        lock (_lock)
        {
            _totalFiles = files;
            _totalBytes = bytes;
            _doneFiles = 0;
            _doneBytes = 0;
        }
    }

    public void AddTotals(int files, long bytes)
    {
        lock (_lock)
        {
            _totalFiles += files;
            _totalBytes += bytes;
        }
    }

    public void BeginFile(string name)
    {
        lock (_lock) _current = name;
    }

    public void AddBytes(long delta)
    {
        lock (_lock)
        {
            _doneBytes += delta;
            if (_doneBytes < 0) _doneBytes = 0;
        }
    }

    public void FileDone()
    {
        lock (_lock) _doneFiles++;
    }

    public ProgressSnapshot Snapshot()
    {
        lock (_lock)
        {
            var now = Stopwatch.GetTimestamp();
            var elapsed = (now - _lastSampleTimestamp) / (double)Stopwatch.Frequency;
            if (elapsed >= 0.5)
            {
                var delta = _doneBytes - _lastSampleBytes;
                var instant = delta / elapsed;
                _speed = _speed <= 0 ? Math.Max(instant, 0) : _speed * 0.6 + Math.Max(instant, 0) * 0.4;
                _lastSampleBytes = _doneBytes;
                _lastSampleTimestamp = now;
            }

            var total = Math.Max(_totalBytes, _doneBytes);
            return new ProgressSnapshot(_phase, _doneBytes, total, _doneFiles, _totalFiles, _speed, _current);
        }
    }
}

public interface IInstallReporter
{
    void Log(string message);
    void Phase(string phase);
    void Progress(ProgressSnapshot snapshot);
}

public sealed class NullReporter : IInstallReporter
{
    public static NullReporter Instance { get; } = new();
    public void Log(string message) { }
    public void Phase(string phase) { }
    public void Progress(ProgressSnapshot snapshot) { }
}

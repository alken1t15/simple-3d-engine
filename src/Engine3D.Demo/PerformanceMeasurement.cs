namespace Engine3D.Demo;

internal readonly record struct MemorySample(long WorkingSetBytes, long ManagedBytes);

/// <summary>Интервальные метрики, не покадровые времена GPU/дисплея.</summary>
internal sealed record PerformanceSample(double FromSeconds, double ToSeconds, long Frames,
    double MaxCallbackGapMs, MemorySample Memory)
{
    public double Seconds => ToSeconds - FromSeconds;
    public double Fps => Frames / Seconds;
    public double? MeanFrameMs => Frames > 0 ? Seconds * 1000 / Frames : null;
}

/// <summary>
/// Чистый CPU-счётчик, тестируемый без ожидания 70 секунд. Время — монотонный Stopwatch,
/// кадры — только RenderedFrames. Финальная точка берётся после возврата Run.
/// </summary>
internal sealed class PerformanceMeasurement(double warmupSeconds, double durationSeconds)
{
    private double _lastTime;
    private long _lastFrames;
    private double _previousCallbackTime;
    private double _sampleTime;
    private long _sampleFrames;
    private double _maxGapMs;
    private bool _finished;

    internal double? StartSeconds { get; private set; }
    internal long StartFrames { get; private set; }
    internal double EndSeconds { get; private set; }
    internal long EndFrames { get; private set; }
    internal bool CloseRequested { get; private set; }
    internal string? InvalidReason { get; private set; }
    internal List<PerformanceSample> Samples { get; } = new();
    internal double MeasuredSeconds => StartSeconds is { } start ? EndSeconds - start : 0;
    internal long MeasuredFrames => StartSeconds.HasValue ? EndFrames - StartFrames : 0;
    internal double? Fps => MeasuredSeconds > 0 ? MeasuredFrames / MeasuredSeconds : null;
    internal double? MeanFrameMs => MeasuredFrames > 0 ? MeasuredSeconds * 1000 / MeasuredFrames : null;
    internal string Status => InvalidReason is not null ? "invalid"
        : !StartSeconds.HasValue ? "cancelled-before-measurement"
        : CloseRequested && MeasuredSeconds >= durationSeconds ? "complete" : "incomplete";

    /// <returns>true, если приложение должно вызвать RequestClose; callbacks не считаются кадрами.</returns>
    internal bool Observe(double seconds, long renderedFrames, Func<MemorySample> readMemory)
    {
        ValidatePoint(seconds, renderedFrames);
        if (InvalidReason is not null)
            return true;
        if (StartSeconds is null)
        {
            if (seconds < warmupSeconds)
                return false;
            StartSeconds = _sampleTime = _previousCallbackTime = seconds;
            StartFrames = _sampleFrames = renderedFrames;
            // Начальная память при t=0 хранится отдельно от интервальных наблюдений.
            StartMemory = readMemory();
        }
        else
        {
            _maxGapMs = Math.Max(_maxGapMs, (seconds - _previousCallbackTime) * 1000);
            _previousCallbackTime = seconds;
            if (seconds - _sampleTime >= 1)
                AddSample(seconds, renderedFrames, readMemory());
        }

        if (seconds - StartSeconds.Value >= durationSeconds)
            CloseRequested = true;
        return CloseRequested;
    }

    internal MemorySample? StartMemory { get; private set; }

    internal void Invalidate(string reason) => InvalidReason ??= reason;

    internal void Finish(double seconds, long renderedFrames, Func<MemorySample> readMemory)
    {
        ValidatePoint(seconds, renderedFrames);
        EndSeconds = seconds;
        EndFrames = renderedFrames;
        if (StartSeconds.HasValue && seconds > _sampleTime)
        {
            _maxGapMs = Math.Max(_maxGapMs, (seconds - _previousCallbackTime) * 1000);
            AddSample(seconds, renderedFrames, readMemory());
        }
        _finished = true;
    }

    private void AddSample(double seconds, long frames, MemorySample memory)
    {
        Samples.Add(new PerformanceSample(_sampleTime - StartSeconds!.Value, seconds - StartSeconds.Value,
            frames - _sampleFrames, _maxGapMs, memory));
        _sampleTime = seconds;
        _sampleFrames = frames;
        _maxGapMs = 0;
    }

    private void ValidatePoint(double seconds, long frames)
    {
        if (_finished || !double.IsFinite(seconds) || seconds < _lastTime || frames < _lastFrames)
            throw new InvalidOperationException("Measurement requires monotonic time/RenderedFrames and one Finish.");
        _lastTime = seconds;
        _lastFrames = frames;
    }
}

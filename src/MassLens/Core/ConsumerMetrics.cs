namespace MassLens.Core;

internal sealed class ConsumerMetrics
{
    public string ConsumerType { get; }
    public string EndpointAddress { get; }

    private long _consumed;
    private long _faulted;
    private int _concurrent;
    private int _peakConcurrent;
    private long _sizeUnder1K;
    private long _size1Kto10K;
    private long _size10Kto100K;
    private long _sizeOver100K;

    private readonly CircularBuffer<long> _throughputWindow;
    private readonly LatencyTracker _latency;

    public ConsumerMetrics(string consumerType, string endpointAddress, int latencyWindow = 1000)
    {
        ConsumerType = consumerType;
        EndpointAddress = endpointAddress;
        _throughputWindow = new CircularBuffer<long>(5000);
        _latency = new LatencyTracker(latencyWindow);
    }

    public void RecordConsumed(TimeSpan duration, long sizeBytes = 0)
    {
        Interlocked.Increment(ref _consumed);
        _throughputWindow.Write(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _latency.Record(duration);
        Interlocked.Decrement(ref _concurrent);

        if (sizeBytes < 1024)             Interlocked.Increment(ref _sizeUnder1K);
        else if (sizeBytes < 10_240)      Interlocked.Increment(ref _size1Kto10K);
        else if (sizeBytes < 102_400)     Interlocked.Increment(ref _size10Kto100K);
        else                              Interlocked.Increment(ref _sizeOver100K);
    }

    public void RecordFaulted(TimeSpan duration)
    {
        Interlocked.Increment(ref _faulted);
        Interlocked.Decrement(ref _concurrent);
        _latency.Record(duration);
    }

    public void RecordPreConsume()
    {
        var current = Interlocked.Increment(ref _concurrent);
        int peak;
        do { peak = _peakConcurrent; if (current <= peak) break; }
        while (Interlocked.CompareExchange(ref _peakConcurrent, current, peak) != peak);
    }

    public double GetThroughput(int windowSeconds = 60)
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-windowSeconds).ToUnixTimeMilliseconds();
        var samples = _throughputWindow.ReadAll();
        return (double)samples.Count(ts => ts >= cutoff) / windowSeconds;
    }

    public ConsumerSnapshot GetSnapshot() => new()
    {
        ConsumerType      = ConsumerType,
        EndpointAddress   = EndpointAddress,
        TotalConsumed     = Interlocked.Read(ref _consumed),
        TotalFaulted      = Interlocked.Read(ref _faulted),
        CurrentConcurrent = _concurrent,
        PeakConcurrent    = _peakConcurrent,
        ThroughputPerSec  = GetThroughput(),
        Latency           = _latency.GetSnapshot(),
        HealthScore       = ComputeHealthScore(),
        SizeUnder1K       = Interlocked.Read(ref _sizeUnder1K),
        Size1Kto10K       = Interlocked.Read(ref _size1Kto10K),
        Size10Kto100K     = Interlocked.Read(ref _size10Kto100K),
        SizeOver100K      = Interlocked.Read(ref _sizeOver100K)
    };

    private int ComputeHealthScore()
    {
        var total = Interlocked.Read(ref _consumed) + Interlocked.Read(ref _faulted);
        if (total == 0) return 100;

        double errorRate = (double)Interlocked.Read(ref _faulted) / total;
        var latency = _latency.GetSnapshot();

        int score = 100;
        score -= (int)(errorRate * 60);
        if (latency.P95 > 5000) score -= 20;
        else if (latency.P95 > 1000) score -= 10;

        return Math.Clamp(score, 0, 100);
    }
}

public sealed record ConsumerSnapshot
{
    public string ConsumerType { get; init; } = "";
    public string EndpointAddress { get; init; } = "";
    public long TotalConsumed { get; init; }
    public long TotalFaulted { get; init; }
    public int CurrentConcurrent { get; init; }
    public int PeakConcurrent { get; init; }
    public double ThroughputPerSec { get; init; }
    public LatencySnapshot Latency { get; init; } = LatencySnapshot.Empty;
    public int HealthScore { get; init; }
    public long SizeUnder1K   { get; init; }
    public long Size1Kto10K   { get; init; }
    public long Size10Kto100K { get; init; }
    public long SizeOver100K  { get; init; }
}

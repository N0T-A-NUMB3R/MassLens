namespace MassLens.Core;

internal sealed class LatencyTracker
{
    private readonly CircularBuffer<double> _samples;

    public LatencyTracker(int windowSize = 1000)
    {
        _samples = new CircularBuffer<double>(windowSize);
    }

    public void Record(TimeSpan duration) => _samples.Write(duration.TotalMilliseconds);

    public LatencySnapshot GetSnapshot()
    {
        var data = _samples.ReadAll();
        if (data.Length == 0) return LatencySnapshot.Empty;

        Array.Sort(data);
        return new LatencySnapshot
        {
            P50 = Percentile(data, 50),
            P95 = Percentile(data, 95),
            P99 = Percentile(data, 99),
            Average = data.Average(),
            SampleCount = data.Length
        };
    }

    private static double Percentile(double[] sorted, int p)
    {
        double idx = (p / 100.0) * (sorted.Length - 1);
        int lo = (int)idx;
        int hi = Math.Min(lo + 1, sorted.Length - 1);
        double frac = idx - lo;
        return sorted[lo] + frac * (sorted[hi] - sorted[lo]);
    }
}

public sealed record LatencySnapshot
{
    public double P50 { get; init; }
    public double P95 { get; init; }
    public double P99 { get; init; }
    public double Average { get; init; }
    public int SampleCount { get; init; }

    public static LatencySnapshot Empty => new();
}

namespace MassLens.Core;

public sealed class ThroughputPredictor
{
    public static readonly ThroughputPredictor Instance = new();

    private readonly CircularBuffer<(DateTimeOffset ts, double rate)> _history = new(120);

    private ThroughputPredictor() { }

    public void Reset() => _history.Clear();

    public void Record(double ratePerSec)
    {
        _history.Write((DateTimeOffset.UtcNow, ratePerSec));
    }

    public PredictorSnapshot GetSnapshot(int horizonMinutes = 120)
    {
        var samples = _history.ReadAll();
        if (samples.Length < 5)
            return new PredictorSnapshot { Historical = [], Predicted = [] };

        var historical = samples.Select(s => s.rate).ToArray();

        var predicted  = new double[horizonMinutes];
        var weights    = Enumerable.Range(1, historical.Length).Select(i => (double)i).ToArray();
        double wSum    = weights.Sum();

        for (int m = 0; m < horizonMinutes; m++)
        {
            double wma = 0;
            for (int i = 0; i < historical.Length; i++)
                wma += historical[i] * weights[i] / wSum;

            double hourFactor = TimeOfDayFactor(DateTimeOffset.UtcNow.AddMinutes(m));
            predicted[m] = Math.Max(0, wma * hourFactor);
        }

        return new PredictorSnapshot
        {
            Historical = historical,
            Predicted  = predicted
        };
    }

    private static double TimeOfDayFactor(DateTimeOffset t)
    {
        int h = t.Hour;
        if (h is >= 0 and < 6)  return 0.3;
        if (h is >= 6 and < 9)  return 0.7;
        if (h is >= 9 and < 18) return 1.0;
        if (h is >= 18 and < 22) return 0.8;
        return 0.4;
    }
}

public sealed class PredictorSnapshot
{
    public double[] Historical { get; init; } = [];
    public double[] Predicted { get; init; } = [];
}

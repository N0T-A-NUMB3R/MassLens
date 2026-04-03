using System.Collections.Concurrent;

namespace MassLens.Core;

public sealed class HeatmapAggregator
{
    public static readonly HeatmapAggregator Instance = new();

    private readonly ConcurrentDictionary<string, int[,]> _buckets = new();

    private HeatmapAggregator() { }

    public void Record(string consumerType)
    {
        var hour = DateTimeOffset.UtcNow.Hour;
        var grid = _buckets.GetOrAdd(consumerType, _ => new int[7, 24]);
        var dayOfWeek = (int)DateTimeOffset.UtcNow.DayOfWeek;
        Interlocked.Increment(ref grid[dayOfWeek, hour]);
    }

    public void Reset() => _buckets.Clear();

    public HeatmapSnapshot GetSnapshot()
    {
        var rows = _buckets.Select(kv =>
        {
            var grid = kv.Value;
            int max = 1;
            for (int d = 0; d < 7; d++)
                for (int h = 0; h < 24; h++)
                    max = Math.Max(max, grid[d, h]);

            var today = (int)DateTimeOffset.UtcNow.DayOfWeek;
            var vals  = new int[24];
            for (int h = 0; h < 24; h++)
                vals[h] = (int)Math.Round(grid[today, h] / (double)max * 5);

            return new HeatmapRow { ConsumerName = kv.Key, HourlyLoad = vals };
        }).ToArray();

        return new HeatmapSnapshot { Rows = rows };
    }
}

public sealed class HeatmapRow
{
    public string ConsumerName { get; init; } = "";
    public int[] HourlyLoad { get; init; } = new int[24];
}

public sealed class HeatmapSnapshot
{
    public HeatmapRow[] Rows { get; init; } = [];
}

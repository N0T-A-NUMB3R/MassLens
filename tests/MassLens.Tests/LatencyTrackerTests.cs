using MassLens.Core;
using System.Reflection;

namespace MassLens.Tests;

public class LatencyTrackerTests
{
    private static LatencyTracker MakeTracker(int windowSize = 1000)
    {
        var ctor = typeof(LatencyTracker)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                            null, [typeof(int)], null)!;
        return (LatencyTracker)ctor.Invoke([windowSize]);
    }

    [Fact]
    public void Empty_tracker_returns_empty_snapshot()
    {
        var tracker = MakeTracker();
        var snap = tracker.GetSnapshot();
        Assert.Equal(0, snap.SampleCount);
        Assert.Equal(0, snap.P50);
        Assert.Equal(0, snap.P95);
        Assert.Equal(0, snap.P99);
        Assert.Equal(0, snap.Average);
    }

    [Fact]
    public void Single_sample_all_percentiles_equal_that_value()
    {
        var tracker = MakeTracker();
        tracker.Record(TimeSpan.FromMilliseconds(100));
        var snap = tracker.GetSnapshot();
        Assert.Equal(100, snap.P50);
        Assert.Equal(100, snap.P95);
        Assert.Equal(100, snap.P99);
        Assert.Equal(100, snap.Average);
    }

    [Fact]
    public void P50_is_median_of_sorted_values()
    {
        var tracker = MakeTracker();
        for (int i = 1; i <= 100; i++)
            tracker.Record(TimeSpan.FromMilliseconds(i));
        var snap = tracker.GetSnapshot();
        Assert.InRange(snap.P50, 49.0, 51.0);
    }

    [Fact]
    public void P95_is_above_95_percent_of_values()
    {
        var tracker = MakeTracker();
        for (int i = 1; i <= 100; i++)
            tracker.Record(TimeSpan.FromMilliseconds(i));
        var snap = tracker.GetSnapshot();
        Assert.True(snap.P95 >= 94.0 && snap.P95 <= 96.0);
    }

    [Fact]
    public void P99_is_near_maximum()
    {
        var tracker = MakeTracker();
        for (int i = 1; i <= 100; i++)
            tracker.Record(TimeSpan.FromMilliseconds(i));
        var snap = tracker.GetSnapshot();
        Assert.True(snap.P99 >= 98.0 && snap.P99 <= 100.0);
    }

    [Fact]
    public void Average_matches_arithmetic_mean()
    {
        var tracker = MakeTracker();
        tracker.Record(TimeSpan.FromMilliseconds(100));
        tracker.Record(TimeSpan.FromMilliseconds(200));
        tracker.Record(TimeSpan.FromMilliseconds(300));
        var snap = tracker.GetSnapshot();
        Assert.Equal(200.0, snap.Average, precision: 1);
    }

    [Fact]
    public void SampleCount_is_correct()
    {
        var tracker = MakeTracker(50);
        for (int i = 0; i < 30; i++)
            tracker.Record(TimeSpan.FromMilliseconds(10));
        Assert.Equal(30, tracker.GetSnapshot().SampleCount);
    }

    [Fact]
    public void Window_overflow_caps_at_capacity()
    {
        var tracker = MakeTracker(10);
        for (int i = 0; i < 20; i++)
            tracker.Record(TimeSpan.FromMilliseconds(i));
        Assert.Equal(10, tracker.GetSnapshot().SampleCount);
    }
}

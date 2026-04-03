using MassLens.Core;
using System.Reflection;

namespace MassLens.Tests;

public class ConsumerMetricsTests
{
    private static ConsumerMetrics Make(string name = "TestConsumer", string ep = "queue:test") =>
        (ConsumerMetrics)typeof(ConsumerMetrics)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                            null, [typeof(string), typeof(string), typeof(int)], null)!
            .Invoke([name, ep, 1000]);

    [Fact]
    public void Initial_snapshot_is_zeroed()
    {
        var m = Make();
        var s = m.GetSnapshot();
        Assert.Equal(0, s.TotalConsumed);
        Assert.Equal(0, s.TotalFaulted);
        Assert.Equal(100, s.HealthScore);
    }

    [Fact]
    public void RecordConsumed_increments_counter()
    {
        var m = Make();
        m.RecordPreConsume();
        m.RecordConsumed(TimeSpan.FromMilliseconds(50));
        Assert.Equal(1, m.GetSnapshot().TotalConsumed);
    }

    [Fact]
    public void RecordFaulted_increments_counter()
    {
        var m = Make();
        m.RecordPreConsume();
        m.RecordFaulted(TimeSpan.FromMilliseconds(10));
        Assert.Equal(1, m.GetSnapshot().TotalFaulted);
    }

    [Fact]
    public void HealthScore_degrades_with_faults()
    {
        var m = Make();
        for (int i = 0; i < 5; i++) { m.RecordPreConsume(); m.RecordConsumed(TimeSpan.FromMilliseconds(10)); }
        for (int i = 0; i < 5; i++) { m.RecordPreConsume(); m.RecordFaulted(TimeSpan.FromMilliseconds(10)); }
        Assert.True(m.GetSnapshot().HealthScore < 100);
    }

    [Fact]
    public void HealthScore_is_zero_when_all_faulted()
    {
        var m = Make();
        for (int i = 0; i < 10; i++) { m.RecordPreConsume(); m.RecordFaulted(TimeSpan.FromMilliseconds(10)); }
        Assert.Equal(40, m.GetSnapshot().HealthScore);
    }

    [Fact]
    public void Size_bucket_under1k_incremented_correctly()
    {
        var m = Make();
        m.RecordPreConsume();
        m.RecordConsumed(TimeSpan.FromMilliseconds(10), sizeBytes: 512);
        Assert.Equal(1, m.GetSnapshot().SizeUnder1K);
        Assert.Equal(0, m.GetSnapshot().Size1Kto10K);
    }

    [Fact]
    public void Size_bucket_1k_to_10k_incremented_correctly()
    {
        var m = Make();
        m.RecordPreConsume();
        m.RecordConsumed(TimeSpan.FromMilliseconds(10), sizeBytes: 5_000);
        Assert.Equal(0, m.GetSnapshot().SizeUnder1K);
        Assert.Equal(1, m.GetSnapshot().Size1Kto10K);
    }

    [Fact]
    public void Size_bucket_10k_to_100k_incremented_correctly()
    {
        var m = Make();
        m.RecordPreConsume();
        m.RecordConsumed(TimeSpan.FromMilliseconds(10), sizeBytes: 50_000);
        Assert.Equal(1, m.GetSnapshot().Size10Kto100K);
    }

    [Fact]
    public void Size_bucket_over_100k_incremented_correctly()
    {
        var m = Make();
        m.RecordPreConsume();
        m.RecordConsumed(TimeSpan.FromMilliseconds(10), sizeBytes: 200_000);
        Assert.Equal(1, m.GetSnapshot().SizeOver100K);
    }

    [Fact]
    public void PeakConcurrent_tracks_maximum()
    {
        var m = Make();
        m.RecordPreConsume();
        m.RecordPreConsume();
        m.RecordPreConsume();
        m.RecordConsumed(TimeSpan.FromMilliseconds(10));
        Assert.Equal(3, m.GetSnapshot().PeakConcurrent);
    }

    [Fact]
    public void Throughput_is_zero_with_no_messages()
    {
        var m = Make();
        Assert.Equal(0.0, m.GetThroughput(), precision: 3);
    }
}

using MassLens.Core;
using System.Reflection;

namespace MassLens.Tests;

public class HeatmapAggregatorTests
{
    private static HeatmapAggregator MakeFresh()
    {
        var ctor = typeof(HeatmapAggregator)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [], null)!;
        return (HeatmapAggregator)ctor.Invoke([]);
    }

    [Fact]
    public void Empty_aggregator_returns_no_rows()
    {
        var agg = MakeFresh();
        Assert.Empty(agg.GetSnapshot().Rows);
    }

    [Fact]
    public void Record_creates_row_for_consumer()
    {
        var agg = MakeFresh();
        agg.Record("OrderConsumer");
        var rows = agg.GetSnapshot().Rows;
        Assert.Single(rows);
        Assert.Equal("OrderConsumer", rows[0].ConsumerName);
    }

    [Fact]
    public void HourlyLoad_has_24_entries()
    {
        var agg = MakeFresh();
        agg.Record("OrderConsumer");
        Assert.Equal(24, agg.GetSnapshot().Rows[0].HourlyLoad.Length);
    }

    [Fact]
    public void HourlyLoad_values_are_in_range_0_to_5()
    {
        var agg = MakeFresh();
        for (int i = 0; i < 50; i++) agg.Record("OrderConsumer");
        var vals = agg.GetSnapshot().Rows[0].HourlyLoad;
        Assert.All(vals, v => Assert.InRange(v, 0, 5));
    }

    [Fact]
    public void Multiple_consumers_tracked_separately()
    {
        var agg = MakeFresh();
        agg.Record("ConsumerA");
        agg.Record("ConsumerB");
        agg.Record("ConsumerA");
        var rows = agg.GetSnapshot().Rows;
        Assert.Equal(2, rows.Length);
    }

    [Fact]
    public void Peak_hour_normalizes_to_5()
    {
        var agg = MakeFresh();
        for (int i = 0; i < 100; i++) agg.Record("OrderConsumer");
        var max = agg.GetSnapshot().Rows[0].HourlyLoad.Max();
        Assert.Equal(5, max);
    }
}

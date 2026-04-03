using MassLens.Core;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MassLens.Tests;

public class HealthCheckTests
{
    private static HealthCheckContext MakeContext() =>
        new()
        {
            Registration = new HealthCheckRegistration(
                "masslens", _ => null!, HealthStatus.Unhealthy, [])
        };

    [Fact]
    public async Task No_consumers_returns_Healthy()
    {
        var check = new MassLensHealthCheckWithSnapshot([]);
        var result = await check.CheckHealthAsync(MakeContext());
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task All_healthy_consumers_returns_Healthy()
    {
        var check = new MassLensHealthCheckWithSnapshot(
        [
            MakeConsumer("A", 90),
            MakeConsumer("B", 85),
        ]);
        var result = await check.CheckHealthAsync(MakeContext());
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Degraded_consumer_returns_Degraded()
    {
        var check = new MassLensHealthCheckWithSnapshot(
        [
            MakeConsumer("A", 90),
            MakeConsumer("B", 60),
        ]);
        var result = await check.CheckHealthAsync(MakeContext());
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("degraded", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Critical_consumer_returns_Unhealthy()
    {
        var check = new MassLensHealthCheckWithSnapshot(
        [
            MakeConsumer("A", 90),
            MakeConsumer("B", 30),
        ]);
        var result = await check.CheckHealthAsync(MakeContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("critical", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Critical_takes_priority_over_degraded()
    {
        var check = new MassLensHealthCheckWithSnapshot(
        [
            MakeConsumer("A", 60),
            MakeConsumer("B", 20),
        ]);
        var result = await check.CheckHealthAsync(MakeContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Data_contains_throughput_and_dlq_keys()
    {
        var check = new MassLensHealthCheckWithSnapshot([MakeConsumer("A", 90)]);
        var result = await check.CheckHealthAsync(MakeContext());
        Assert.True(result.Data.ContainsKey("throughput/s"));
        Assert.True(result.Data.ContainsKey("dlqTotal"));
        Assert.True(result.Data.ContainsKey("consumers"));
    }

    private static ConsumerSnapshot MakeConsumer(string name, int score) => new()
    {
        ConsumerType     = name,
        EndpointAddress  = "queue:test",
        HealthScore      = score,
        Latency          = LatencySnapshot.Empty
    };
}

// Testable subclass that injects a fake snapshot instead of reading MessageStore.Instance
internal sealed class MassLensHealthCheckWithSnapshot(ConsumerSnapshot[] consumers) : MassLensHealthCheck
{
    private readonly ConsumerSnapshot[] _consumers = consumers;

    protected override DashboardSnapshot GetCurrentSnapshot() => new()
    {
        Consumers  = _consumers,
        DlqGroups  = [],
        Sagas      = [],
        AuditLog   = [],
        Heatmap    = new HeatmapSnapshot(),
        Predictor  = new PredictorSnapshot()
    };
}

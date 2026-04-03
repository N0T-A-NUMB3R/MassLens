using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MassLens.Core;

public class MassLensHealthCheck : IHealthCheck
{
    protected virtual DashboardSnapshot GetCurrentSnapshot() => MessageStore.Instance.GetSnapshot();

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var snapshot = GetCurrentSnapshot();

        var consumers = snapshot.Consumers;
        if (consumers.Length == 0)
            return Task.FromResult(HealthCheckResult.Healthy("No consumers observed yet."));

        var critical = consumers.Where(c => c.HealthScore < 50).ToArray();
        var degraded = consumers.Where(c => c.HealthScore >= 50 && c.HealthScore < 80).ToArray();

        var data = new Dictionary<string, object>
        {
            ["totalConsumed"]  = snapshot.TotalConsumed,
            ["totalFaulted"]   = snapshot.TotalFaulted,
            ["throughput/s"]   = Math.Round(snapshot.GlobalThroughput, 2),
            ["totalFaulted"]    = snapshot.TotalFaulted,
            ["consumers"]      = consumers.Length,
            ["criticalCount"]  = critical.Length,
            ["degradedCount"]  = degraded.Length,
        };

        if (critical.Length > 0)
        {
            data["criticalConsumers"] = critical.Select(c => c.ConsumerType).ToArray();
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"{critical.Length} consumer(s) critical (score < 50)", data: data));
        }

        if (degraded.Length > 0)
        {
            data["degradedConsumers"] = degraded.Select(c => c.ConsumerType).ToArray();
            return Task.FromResult(HealthCheckResult.Degraded(
                $"{degraded.Length} consumer(s) degraded (score < 80)", data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("All consumers healthy.", data));
    }
}

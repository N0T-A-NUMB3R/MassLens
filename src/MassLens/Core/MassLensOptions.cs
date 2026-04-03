namespace MassLens.Core;

public sealed class MassLensOptions
{
    public bool Enabled { get; set; } = true;
    public bool ReadOnly { get; set; } = false;
    public string? AuthorizationPolicy { get; set; }
    public string[] AllowedIPs { get; set; } = [];
    public string? HeaderToken { get; set; }
    public bool DisableInProduction { get; set; } = true;
    public string BasePath { get; set; } = "/masslens";
    public int ChannelCapacity { get; set; } = 10_000;
    public int RecentEntriesCapacity { get; set; } = 500;
    /// <summary>Maximum concurrent SSE connections. Default: 10.</summary>
    public int MaxSseConnections { get; set; } = 10;

    /// <summary>
    /// How long to retain in-memory metrics before resetting counters and clearing trace history.
    /// Set to 0 to disable automatic reset. Default: 0 (no reset).
    /// </summary>
    public int MetricsRetentionHours { get; set; } = 0;

    /// <summary>
    /// POST a JSON payload to this URL when the error rate exceeds <see cref="AlertErrorRateThreshold"/>.
    /// Leave null to disable webhook alerts.
    /// </summary>
    public string? AlertWebhookUrl { get; set; }

    /// <summary>Error rate % (0–100) that triggers the webhook. Default: 10.</summary>
    public double AlertErrorRateThreshold { get; set; } = 10.0;
}

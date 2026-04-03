# MassLens

**The missing dashboard for MassTransit. Zero dependencies. A few lines of code. Full visibility.**

MassLens is an embedded real-time monitoring dashboard for MassTransit v8 applications. It ships as a single NuGet package — no Redis, no SQL Server, no external services required. Tested with RabbitMQ.

---

## Quick Start

```bash
dotnet add package MassLens
```

```csharp
// Program.cs
builder.Services.AddMassTransit(x =>
{
    x.AddMassLens(); // registers consume/publish/send/saga observers
    x.AddConsumer<OrderSubmittedConsumer>();
    x.UsingRabbitMq((ctx, cfg) => cfg.ConfigureEndpoints(ctx));
});

builder.Services.AddMassLensUI(options =>
{
    options.ReadOnly            = false;
    options.DisableInProduction = true;
    options.AuthorizationPolicy = "MassLensPolicy"; // optional
});

app.UseMassLens(); // serves dashboard at /masslens
```

Open `http://localhost:5000/masslens` — that's it.

---

## What You Get

| Tab | Features |
|---|---|
| **Overview** | Throughput live, consumed/published/sent counters, latency P50/P95/P99, consumer inventory with health scores, throughput predictor |
| **Saga Flow** | Visual state machine diagram with live instance counts per state, drill-down by CorrelationId, saga export (JSON/CSV) |
| **Timeline** | Flame graph per CorrelationId, retry waterfall with timing, message body inspector |
| **Topology** | Auto-generated publisher → consumer graph with animated message flow |
| **Analytics** | Heatmap 24h × consumer, message size distribution, top 10 slowest consumers, top 10 message types by volume |

---

## Configuration

```csharp
builder.Services.AddMassLensUI(options =>
{
    // Access control
    options.AllowedIPs          = ["127.0.0.1"];
    options.HeaderToken         = "your-secret-token";  // X-MassLens-Token header
    options.AuthorizationPolicy = "MassLensAdmins";     // ASP.NET Core policy

    // Behavior
    options.ReadOnly            = true;   // stakeholder view — no message injection
    options.Enabled             = true;
    options.DisableInProduction = true;   // default: dashboard 404s in Production
    options.BasePath            = "/masslens";

    // Buffer sizing (tune for high-throughput apps)
    options.ChannelCapacity        = 50_000;  // default 10,000
    options.RecentEntriesCapacity  = 2_000;   // default 500

    // Metrics retention — reset all counters every N hours (0 = never)
    options.MetricsRetentionHours  = 24;

    // Threshold alerts — POST to a webhook when error rate exceeds the threshold
    options.AlertWebhookUrl           = "https://hooks.slack.com/services/...";
    options.AlertErrorRateThreshold   = 10.0; // default: 10%
});
```

---

## Health Check

MassLens registers an `IHealthCheck` automatically. Wire it to the standard ASP.NET Core endpoint:

```csharp
app.MapHealthChecks("/health");
```

The check returns:
- **Healthy** — all consumers have a health score >= 80
- **Degraded** — at least one consumer between 50-79
- **Unhealthy** — at least one consumer below 50

---

## How It Works

MassLens hooks into MassTransit using native observer interfaces:

- `IConsumeObserver` — tracks every message consumed and faulted, including saga transitions
- `IPublishObserver` — counts published messages
- `ISendObserver` — counts sent messages

All data is kept in bounded in-memory structures (circular buffers, `Channel<T>`). No data leaves your process. The dashboard is served as an embedded HTML resource directly from the DLL.

Live updates are delivered via **SSE (Server-Sent Events)** at `/masslens/stream` — no WebSockets, no polling.

---

## Transport Support

| Transport | Status |
|---|---|
| RabbitMQ | Tested |
| Azure Service Bus | Compatible (untested) |
| Amazon SQS | Compatible (untested) |
| In-Memory | Tested |

---

## Requirements

- .NET 8+
- MassTransit 8.x
- ASP.NET Core (middleware pipeline)

---

## License

MIT — https://github.com/N0T-A-NUMB3R/MassLens

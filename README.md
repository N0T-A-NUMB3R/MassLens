# MassLens

[![NuGet](https://img.shields.io/nuget/v/MassLens?color=blue&logo=nuget)](https://www.nuget.org/packages/MassLens)
[![NuGet Downloads](https://img.shields.io/nuget/dt/MassLens?color=blue)](https://www.nuget.org/packages/MassLens)
[![Build](https://img.shields.io/github/actions/workflow/status/N0T-A-NUMB3R/MassLens/build.yml?branch=main&logo=github)](https://github.com/N0T-A-NUMB3R/MassLens/actions)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8%2B-512BD4&logo=dotnet)](https://dotnet.microsoft.com)
[![MassTransit](https://img.shields.io/badge/MassTransit-8.x-purple)](https://masstransit.io)

**The missing dashboard for MassTransit. Zero dependencies. A few lines of code. Full visibility.**

MassLens is an embedded real-time monitoring dashboard for MassTransit v8 applications. It ships as a single NuGet package — no Redis, no SQL Server, no external services required.

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

### Read-Only Mode

Enable for Product Owners and stakeholders — they can see everything but cannot inject test messages.

```csharp
options.ReadOnly = true;
```

### Authorization Policy

```csharp
builder.Services.AddAuthorization(auth =>
{
    auth.AddPolicy("MassLensAdmins", policy =>
        policy.RequireRole("dev", "ops"));
});

builder.Services.AddMassLensUI(options =>
{
    options.AuthorizationPolicy = "MassLensAdmins";
});
```

### IP Whitelist + CI/CD Header Token

```csharp
options.AllowedIPs  = ["10.0.0.100"];
options.HeaderToken = Environment.GetEnvironmentVariable("MASSLENS_TOKEN");
```

Pass `X-MassLens-Token: your-token` from your CI/CD pipeline or health check script.

---

## Health Check

MassLens registers an `IHealthCheck` automatically. Wire it to the standard ASP.NET Core endpoint:

```csharp
app.MapHealthChecks("/health");
```

The check returns:
- **Healthy** — all consumers have a health score ≥ 80
- **Degraded** — at least one consumer between 50–79
- **Unhealthy** — at least one consumer below 50

The response body includes `throughput/s`, `totalFaulted`, and the list of critical/degraded consumers.

---

## How It Works

MassLens hooks into MassTransit using native observer interfaces:

- `IConsumeObserver` — tracks every message consumed and faulted, including saga transitions
- `IPublishObserver` — counts published messages
- `ISendObserver` — counts sent messages

All data is kept in bounded in-memory structures (circular buffers, `Channel<T>`). No data leaves your process. The dashboard is served as an embedded HTML resource directly from the DLL.

Live updates are delivered via **SSE (Server-Sent Events)** at `/masslens/stream` — no WebSockets, no polling.

---

## Endpoints

| Path | Description |
|---|---|
| `GET /masslens` | Dashboard UI |
| `GET /masslens/stream` | SSE stream — `DashboardSnapshot` every second |
| `GET /masslens/snapshot` | One-shot JSON snapshot |
| `POST /masslens/inject` | Inject a test message (Development only, blocked by ReadOnly) |
| `GET /masslens/trace` | Recent correlation IDs with consumed entries |
| `GET /masslens/trace?correlationId=…` | Flame graph entries for a single conversation |
| `GET /masslens/saga/export?format=csv` | Export active saga instances as CSV |
| `GET /masslens/saga/export?format=json` | Export active saga instances as JSON |

Inject payload:
```json
{
  "messageType": "OrderSubmitted",
  "payload": { "orderId": "TEST-001", "customerId": "CUST-999" }
}
```

---

## Transport Support

MassLens observes at the MassTransit abstraction layer — it works with any transport:

- RabbitMQ
- Azure Service Bus
- Amazon SQS
- In-Memory (tests)

---

## Load Test — Demo Project

The repository includes `demo/MassLens.Demo` — a self-contained ASP.NET Core app that generates realistic high-volume traffic with no external dependencies (uses the MassTransit in-memory transport).

> **Note on latency figures:** the demo runs all consumers in-process on the in-memory transport, sharing a single thread pool with the load generator. Latency values (P95 > 1 s) reflect thread-pool contention under extreme synthetic load, not real broker round-trips. In production with RabbitMQ or Azure Service Bus, consumer latency is typically 5–100 ms.

```bash
cd demo/MassLens.Demo
dotnet run
# open http://localhost:5100/masslens
```

### What the demo generates

**Phase 1 — Order burst (≈12 seconds)**

3,000 orders submitted in batches of 50 every 200 ms. Each order triggers a full saga flow:

```
OrderSubmitted → ProcessPayment → ReserveInventory → ShipOrder → SendNotification
```

This produces approximately **18,000–24,000 saga-related messages**.

**Phase 2 — Continuous background load**

500 messages/second of stock checks and analytics events, running indefinitely.

**After 2 minutes of runtime: >70,000 messages processed.**

### Topology

| Consumer | Behaviour | Visible in MassLens |
|---|---|---|
| `OrderSubmittedConsumer` | Fast, 5–40 ms | Healthy, high throughput |
| `PaymentConsumer` | 15% soft failure → retry, 5% exception | Degraded score, retries visible |
| `InventoryConsumer` | Slow, 80–350 ms | P95 bottleneck, red in Analytics |
| `ShippingConsumer` | Medium, 30–90 ms | Healthy |
| `NotificationConsumer` | Fast, 5–25 ms | Healthy |
| `StockConsumer` | High volume, 2–15 ms | Top message type by volume |
| `TrackEventConsumer` | 2% exception | Faulted, visible in Timeline |

### Saga: `OrderStateMachine`

6-state machine with live transitions visible in the Saga Flow tab:

```
Initial → PaymentPending → InventoryPending → Shipping → Completed
                        ↘ PaymentFailed (retry ×3) → Cancelled
                                          ↘ InventoryUnavailable → Cancelled
```

---

## Unit Tests

```bash
dotnet test tests/MassLens.Tests/
```

63 tests covering:

| Suite | Tests |
|---|---|
| `CircularBufferTests` | Overflow, order preservation, thread safety, edge cases |
| `LatencyTrackerTests` | P50/P95/P99 calculation, window overflow, empty state |
| `ConsumerMetricsTests` | Health score, size buckets, concurrency tracking |
| `SagaMetricsTests` | State transitions, instance tracking, fault/completion counters |
| `HealthCheckTests` | Healthy/Degraded/Unhealthy classification, data payload |
| `HeatmapAggregatorTests` | 24-column output, normalization 0–5, multi-consumer |
| `MassLensOptionsTests` | Default values for all configuration properties |

---

## Requirements

- .NET 8+
- MassTransit 8.x
- ASP.NET Core (middleware pipeline)

---

## Acknowledgements

Inspired by [Hangfire Dashboard](https://www.hangfire.io/) — the gold standard for embedded job monitoring in ASP.NET Core. MassLens brings the same zero-friction, embedded-first philosophy to MassTransit message bus observability.

---

## License

MIT

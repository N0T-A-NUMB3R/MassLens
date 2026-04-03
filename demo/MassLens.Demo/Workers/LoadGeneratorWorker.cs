using MassLens.Demo.Contracts;
using MassTransit;

namespace MassLens.Demo.Workers;

public class LoadGeneratorWorker(IBus bus, ILogger<LoadGeneratorWorker> logger) : BackgroundService
{
    private static readonly string[] CustomerIds = ["CUST-001", "CUST-002", "CUST-003", "CUST-042", "CUST-099"];
    private static readonly string[] Skus = ["SKU-001", "SKU-002", "SKU-003", "SKU-007", "SKU-042", "SKU-999"];

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // give the bus time to start
        await Task.Delay(2000, ct);

        logger.LogInformation("Load generator started — target: >10,000 messages");

        // Phase 1: burst of order submissions (main saga flow)
        await RunOrderBurst(ct);

        // Phase 2: continuous background load (stock checks + analytics)
        await RunContinuousLoad(ct);
    }

    private async Task RunOrderBurst(CancellationToken ct)
    {
        const int batchSize = 50;
        const int batches   = 60; // 3000 orders → each triggers 5-8 messages = ~15k-24k total

        logger.LogInformation("Phase 1: submitting {Total} orders in {Batches} batches",
            batchSize * batches, batches);

        for (int b = 0; b < batches && !ct.IsCancellationRequested; b++)
        {
            var tasks = Enumerable.Range(0, batchSize).Select(_ => SubmitOrder(ct));
            await Task.WhenAll(tasks);
            await Task.Delay(200, ct); // 200ms between batches → ~5 batches/sec
        }

        logger.LogInformation("Phase 1 complete");
    }

    private async Task RunContinuousLoad(CancellationToken ct)
    {
        logger.LogInformation("Phase 2: continuous stock + analytics load");

        while (!ct.IsCancellationRequested)
        {
            var tasks = new List<Task>();

            // 20 stock checks per tick
            for (int i = 0; i < 20; i++)
            {
                var sku = Skus[Random.Shared.Next(Skus.Length)];
                tasks.Add(bus.Publish(new CheckStock(sku, Random.Shared.Next(1, 50)), ct));
            }

            // 30 analytics events per tick
            for (int i = 0; i < 30; i++)
            {
                tasks.Add(bus.Publish(new TrackEvent(
                    RandomEventType(),
                    Guid.NewGuid().ToString("N"),
                    new Dictionary<string, string>
                    {
                        ["source"]    = "demo",
                        ["sessionId"] = Guid.NewGuid().ToString("N")[..8]
                    }), ct));
            }

            await Task.WhenAll(tasks);
            await Task.Delay(100, ct); // 10 ticks/sec → 500 msg/sec continuous
        }
    }

    private async Task SubmitOrder(CancellationToken ct)
    {
        var orderId    = Guid.NewGuid();
        var customerId = CustomerIds[Random.Shared.Next(CustomerIds.Length)];
        var items      = Skus.OrderBy(_ => Random.Shared.Next()).Take(Random.Shared.Next(1, 4)).ToArray();
        var total      = Math.Round((decimal)(Random.Shared.NextDouble() * 500 + 10), 2);

        await bus.Publish(new OrderSubmitted(orderId, customerId, total, DateTimeOffset.UtcNow), ct);
    }

    private static string RandomEventType() => Random.Shared.Next(5) switch
    {
        0 => "page_view",
        1 => "button_click",
        2 => "search",
        3 => "checkout_start",
        _ => "purchase_complete"
    };
}

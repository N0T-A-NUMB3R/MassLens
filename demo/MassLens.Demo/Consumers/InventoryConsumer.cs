using MassLens.Demo.Contracts;
using MassTransit;

namespace MassLens.Demo.Consumers;

public class InventoryConsumer(ILogger<InventoryConsumer> logger) : IConsumer<ReserveInventory>
{
    private static int _callCount;

    public async Task Consume(ConsumeContext<ReserveInventory> context)
    {
        // Intentionally slow to show up as P95 bottleneck in MassLens
        await Task.Delay(Random.Shared.Next(80, 350));

        var count = Interlocked.Increment(ref _callCount);

        // ~8% inventory unavailable
        if (count % 12 == 0)
        {
            var missing = context.Message.Items.Take(1).ToArray();
            logger.LogWarning("Inventory unavailable for {Items}", string.Join(",", missing));
            await context.Publish(new InventoryUnavailable(context.Message.OrderId, missing));
            return;
        }

        logger.LogDebug("Inventory reserved for order {OrderId}", context.Message.OrderId);
        await context.Publish(new InventoryReserved(context.Message.OrderId, context.Message.Items));
    }
}

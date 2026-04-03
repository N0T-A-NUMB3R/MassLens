using MassLens.Demo.Contracts;
using MassTransit;

namespace MassLens.Demo.Consumers;

// High-volume consumer — runs in parallel to simulate realistic topology
public class StockConsumer : IConsumer<CheckStock>
{
    public async Task Consume(ConsumeContext<CheckStock> context)
    {
        await Task.Delay(Random.Shared.Next(2, 15));
        var available = context.Message.RequiredQuantity <= Random.Shared.Next(0, 200);
        await context.Publish(new StockChecked(
            context.Message.Sku, available, Random.Shared.Next(0, 200)));
    }
}

public class StockCheckedConsumer : IConsumer<StockChecked>
{
    public async Task Consume(ConsumeContext<StockChecked> context)
    {
        await Task.Delay(2);
    }
}

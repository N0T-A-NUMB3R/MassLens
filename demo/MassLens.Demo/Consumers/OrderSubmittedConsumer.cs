using MassLens.Demo.Contracts;
using MassTransit;

namespace MassLens.Demo.Consumers;

public class OrderSubmittedConsumer(ILogger<OrderSubmittedConsumer> logger) : IConsumer<OrderSubmitted>
{
    public async Task Consume(ConsumeContext<OrderSubmitted> context)
    {
        await Task.Delay(Random.Shared.Next(5, 40));
        logger.LogDebug("Order {OrderId} submitted by {CustomerId}", context.Message.OrderId, context.Message.CustomerId);
    }
}

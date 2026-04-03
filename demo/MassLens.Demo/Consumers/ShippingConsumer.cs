using MassLens.Demo.Contracts;
using MassTransit;

namespace MassLens.Demo.Consumers;

public class ShippingConsumer(ILogger<ShippingConsumer> logger) : IConsumer<ShipOrder>
{
    public async Task Consume(ConsumeContext<ShipOrder> context)
    {
        await Task.Delay(Random.Shared.Next(30, 90));
        var tracking = $"TRK-{context.Message.OrderId:N}".Substring(0, 16).ToUpper();
        logger.LogDebug("Order {OrderId} shipped with tracking {Tracking}", context.Message.OrderId, tracking);
        await context.Publish(new OrderShipped(context.Message.OrderId, tracking, DateTimeOffset.UtcNow));
    }
}

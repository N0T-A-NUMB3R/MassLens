using MassLens.Demo.Contracts;
using MassTransit;

namespace MassLens.Demo.Consumers;

public class NotificationConsumer(ILogger<NotificationConsumer> logger) : IConsumer<SendNotification>
{
    public async Task Consume(ConsumeContext<SendNotification> context)
    {
        await Task.Delay(Random.Shared.Next(5, 25));
        logger.LogDebug("Notification sent via {Channel} for order {OrderId}",
            context.Message.Channel, context.Message.OrderId);
        await context.Publish(new NotificationSent(context.Message.OrderId, context.Message.Channel));
    }
}

using MassLens.Demo.Contracts;
using MassTransit;

namespace MassLens.Demo.Consumers;

public class PaymentConsumer(ILogger<PaymentConsumer> logger) : IConsumer<ProcessPayment>
{
    private static int _callCount;

    public async Task Consume(ConsumeContext<ProcessPayment> context)
    {
        await Task.Delay(Random.Shared.Next(20, 120));

        var count = Interlocked.Increment(ref _callCount);

        // ~15% failure rate to generate retries and DLQ entries
        if (count % 7 == 0)
        {
            logger.LogWarning("Payment declined for order {OrderId}", context.Message.OrderId);
            await context.Publish(new PaymentFailed(context.Message.OrderId, "Card declined"));
            return;
        }

        // ~5% hard fault to simulate exceptions landing in DLQ
        if (count % 19 == 0)
            throw new InvalidOperationException($"Payment gateway timeout for order {context.Message.OrderId}");

        logger.LogDebug("Payment processed for order {OrderId}", context.Message.OrderId);
        await context.Publish(new PaymentProcessed(
            context.Message.OrderId, context.Message.Amount, DateTimeOffset.UtcNow));
    }
}

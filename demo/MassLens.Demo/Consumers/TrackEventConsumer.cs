using MassLens.Demo.Contracts;
using MassTransit;

namespace MassLens.Demo.Consumers;

// Fire-and-forget analytics — very high volume, very fast
public class TrackEventConsumer : IConsumer<TrackEvent>
{
    private static int _faultCount;

    public async Task Consume(ConsumeContext<TrackEvent> context)
    {
        await Task.Delay(1);

        // occasional fault to populate DLQ with a different exception type
        if (Interlocked.Increment(ref _faultCount) % 50 == 0)
            throw new ArgumentException($"Invalid event type: {context.Message.EventType}");
    }
}

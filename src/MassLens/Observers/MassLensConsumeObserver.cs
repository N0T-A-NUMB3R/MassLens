using MassLens.Core;
using MassTransit;

namespace MassLens.Observers;

internal sealed class MassLensConsumeObserver : IConsumeObserver
{
    private readonly MessageStore _store = MessageStore.Instance;

    public Task PreConsume<T>(ConsumeContext<T> context) where T : class
    {
        _store.RecordPreConsume(typeof(T).Name, context.ReceiveContext.InputAddress.ToString());
        return Task.CompletedTask;
    }

    public Task PostConsume<T>(ConsumeContext<T> context) where T : class
    {
        var duration = context.ReceiveContext.ElapsedTime;
        _store.Write(new MessageEntry
        {
            MessageType     = typeof(T).Name,
            ConsumerType    = typeof(T).Name,
            EndpointAddress = context.ReceiveContext.InputAddress.ToString(),
            Direction       = MessageDirection.Consumed,
            Duration        = duration,
            SizeBytes       = context.ReceiveContext.Body.Length ?? 0,
            CorrelationId   = context.ConversationId?.ToString() ?? context.CorrelationId?.ToString()
        });
        return Task.CompletedTask;
    }

    public Task ConsumeFault<T>(ConsumeContext<T> context, Exception exception) where T : class
    {
        var duration = context.ReceiveContext.ElapsedTime;
        _store.Write(new MessageEntry
        {
            MessageType      = typeof(T).Name,
            ConsumerType     = typeof(T).Name,
            EndpointAddress  = context.ReceiveContext.InputAddress.ToString(),
            Direction        = MessageDirection.Faulted,
            Duration         = duration,
            SizeBytes        = context.ReceiveContext.Body.Length ?? 0,
            CorrelationId    = context.ConversationId?.ToString() ?? context.CorrelationId?.ToString(),
            ExceptionType    = exception.GetType().Name,
            ExceptionMessage = exception.Message
        });
        return Task.CompletedTask;
    }
}

using MassLens.Core;
using MassTransit;

namespace MassLens.Observers;

internal sealed class MassLensSendObserver : ISendObserver
{
    private readonly MessageStore _store = MessageStore.Instance;

    public Task PreSend<T>(SendContext<T> context) where T : class =>
        Task.CompletedTask;

    public Task PostSend<T>(SendContext<T> context) where T : class
    {
        _store.Write(new MessageEntry
        {
            MessageType   = typeof(T).Name,
            Direction     = MessageDirection.Sent,
            CorrelationId = context.ConversationId?.ToString() ?? context.CorrelationId?.ToString()
        });
        return Task.CompletedTask;
    }

    public Task SendFault<T>(SendContext<T> context, Exception exception) where T : class
    {
        _store.Write(new MessageEntry
        {
            MessageType      = typeof(T).Name,
            Direction        = MessageDirection.Faulted,
            ExceptionType    = exception.GetType().Name,
            ExceptionMessage = exception.Message,
            CorrelationId    = context.ConversationId?.ToString() ?? context.CorrelationId?.ToString()
        });
        return Task.CompletedTask;
    }
}

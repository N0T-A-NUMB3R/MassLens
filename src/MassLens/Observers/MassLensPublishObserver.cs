using MassLens.Core;
using MassTransit;

namespace MassLens.Observers;

internal sealed class MassLensPublishObserver : IPublishObserver
{
    private readonly MessageStore _store = MessageStore.Instance;

    public Task PrePublish<T>(PublishContext<T> context) where T : class =>
        Task.CompletedTask;

    public Task PostPublish<T>(PublishContext<T> context) where T : class
    {
        _store.Write(new MessageEntry
        {
            MessageType   = typeof(T).Name,
            Direction     = MessageDirection.Published,
            CorrelationId = context.ConversationId?.ToString() ?? context.CorrelationId?.ToString()
        });
        return Task.CompletedTask;
    }

    public Task PublishFault<T>(PublishContext<T> context, Exception exception) where T : class
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

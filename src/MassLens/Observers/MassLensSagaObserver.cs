using MassLens.Core;
using MassTransit;

namespace MassLens.Observers;

public sealed class MassLensSagaConsumeObserver : IConsumeObserver
{
    public Task PreConsume<T>(ConsumeContext<T> context) where T : class =>
        Task.CompletedTask;

    public Task PostConsume<T>(ConsumeContext<T> context) where T : class
    {
        RecordIfSaga(context, isFault: false);
        return Task.CompletedTask;
    }

    public Task ConsumeFault<T>(ConsumeContext<T> context, Exception exception) where T : class
    {
        RecordIfSaga(context, isFault: true);
        return Task.CompletedTask;
    }

    private static void RecordIfSaga<T>(ConsumeContext<T> context, bool isFault) where T : class
    {
        var sagaInterface = context.GetType()
            .GetInterfaces()
            .FirstOrDefault(i =>
                i.IsGenericType &&
                i.GetGenericTypeDefinition().Name.Contains("SagaConsumeContext",
                    StringComparison.OrdinalIgnoreCase));

        if (sagaInterface is null)
            return;

        var sagaInstanceType = sagaInterface.GetGenericArguments().FirstOrDefault();
        var sagaType = sagaInstanceType?.Name ?? "UnknownSaga";
        var correlationId = context.CorrelationId?.ToString() ?? Guid.NewGuid().ToString();

        // Get Saga property — may be explicit interface impl, try the interface directly
        object? sagaObj = null;
        if (sagaInstanceType is not null)
        {
            var sagaProp = sagaInterface.GetProperty("Saga")
                        ?? context.GetType().GetProperty("Saga");
            if (sagaProp is not null)
                sagaObj = sagaProp.GetValue(context);
        }

        var stateProp = sagaObj?.GetType().GetProperty("CurrentState");
        var state     = stateProp?.GetValue(sagaObj)?.ToString()
                        ?? (isFault ? "Faulted" : "Active");

        var isCompleted = state is "Final" or "Completed";

        MessageStore.Instance.GetOrAddSaga(sagaType)
            .RecordTransition(correlationId, fromState: "", toState: state, isFault, isCompleted);
    }
}

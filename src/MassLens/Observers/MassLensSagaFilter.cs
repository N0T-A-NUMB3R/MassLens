using MassLens.Core;
using MassTransit;

namespace MassLens.Observers;

public sealed class MassLensSagaFilter<TSaga, TMessage> : IFilter<ConsumeContext<TMessage>>
    where TSaga : class, ISaga, SagaStateMachineInstance
    where TMessage : class
{
    public void Probe(ProbeContext context) =>
        context.CreateFilterScope("masslens");

    public async Task Send(ConsumeContext<TMessage> context, IPipe<ConsumeContext<TMessage>> next)
    {
        var correlationId = context.CorrelationId?.ToString() ?? Guid.NewGuid().ToString();
        var sagaName      = typeof(TSaga).Name;

        bool faulted = false;
        try
        {
            await next.Send(context);
        }
        catch
        {
            faulted = true;
            throw;
        }
        finally
        {
            var state = faulted ? "Faulted" : "Active";

            if (context is SagaConsumeContext<TSaga> sagaCtx)
            {
                var prop = typeof(TSaga).GetProperty("CurrentState")
                        ?? typeof(TSaga).GetProperty("State");
                state = prop?.GetValue(sagaCtx.Saga)?.ToString() ?? state;
            }

            var isCompleted = state is "Final" or "Completed";

            MessageStore.Instance.GetOrAddSaga(sagaName)
                .RecordTransition(correlationId, fromState: "", toState: state, faulted, isCompleted);
        }
    }
}

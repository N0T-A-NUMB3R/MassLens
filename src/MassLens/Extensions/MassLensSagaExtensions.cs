using MassLens.Observers;
using MassTransit;
using MassTransit.Configuration;

namespace MassLens.Extensions;

public static class MassLensSagaExtensions
{
    public static ISagaConfigurator<TSaga> ObserveWithMassLens<TSaga, TMessage>(
        this ISagaConfigurator<TSaga> configurator)
        where TSaga : class, ISaga, SagaStateMachineInstance
        where TMessage : class
    {
        configurator.Message<TMessage>(m =>
            m.UseFilter(new MassLensSagaFilter<TSaga, TMessage>()));

        return configurator;
    }
}

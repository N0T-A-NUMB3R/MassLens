using MassLens.Core;
using MassLens.Demo.Contracts;
using MassTransit;

namespace MassLens.Demo.Sagas;

public class OrderState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public decimal Total { get; set; }
    public string[] Items { get; set; } = [];
    public int RetryCount { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class OrderStateMachine : MassTransitStateMachine<OrderState>
{
    public State Submitted { get; private set; } = null!;
    public State PaymentPending { get; private set; } = null!;
    public State PaymentFailed { get; private set; } = null!;
    public State InventoryPending { get; private set; } = null!;
    public State Shipping { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Cancelled { get; private set; } = null!;

    public Event<OrderSubmitted> OrderSubmittedEvent { get; private set; } = null!;
    public Event<PaymentProcessed> PaymentProcessedEvent { get; private set; } = null!;
    public Event<PaymentFailed> PaymentFailedEvent { get; private set; } = null!;
    public Event<InventoryReserved> InventoryReservedEvent { get; private set; } = null!;
    public Event<InventoryUnavailable> InventoryUnavailableEvent { get; private set; } = null!;
    public Event<OrderShipped> OrderShippedEvent { get; private set; } = null!;
    public Event<CancelOrder> CancelOrderEvent { get; private set; } = null!;

    // Records every saga transition directly into MassLens
    private static void Track(BehaviorContext<OrderState> ctx, bool isFault = false)
    {
        var state = ctx.Saga.CurrentState ?? "Unknown";
        var isCompleted = state is "Final" or "Completed";
        MessageStore.Instance
            .GetOrAddSaga(nameof(OrderStateMachine))
            .RecordTransition(ctx.Saga.CorrelationId.ToString(), "", state, isFault, isCompleted);
    }

    public OrderStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => OrderSubmittedEvent,       x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => PaymentProcessedEvent,     x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => PaymentFailedEvent,        x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => InventoryReservedEvent,    x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => InventoryUnavailableEvent, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => OrderShippedEvent,         x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => CancelOrderEvent,          x => x.CorrelateById(m => m.Message.OrderId));

        Initially(
            When(OrderSubmittedEvent)
                .Then(ctx =>
                {
                    ctx.Saga.CustomerId  = ctx.Message.CustomerId;
                    ctx.Saga.Total       = ctx.Message.Total;
                    ctx.Saga.SubmittedAt = ctx.Message.SubmittedAt;
                })
                .Then(ctx => ctx.Publish(new ProcessPayment(
                    ctx.Message.OrderId, ctx.Message.CustomerId,
                    ctx.Message.Total, "CreditCard")))
                .TransitionTo(PaymentPending)
                .Then(ctx => Track(ctx))
        );

        During(PaymentPending,
            When(PaymentProcessedEvent)
                .Then(ctx => ctx.Publish(new ReserveInventory(
                    ctx.Message.OrderId,
                    ctx.Saga.Items.Length > 0 ? ctx.Saga.Items : ["SKU-001"])))
                .TransitionTo(InventoryPending)
                .Then(ctx => Track(ctx)),

            When(PaymentFailedEvent)
                .Then(ctx => ctx.Saga.RetryCount++)
                .IfElse(ctx => ctx.Saga.RetryCount < 3,
                    retry => retry
                        .Then(ctx => ctx.Publish(new ProcessPayment(
                            ctx.Message.OrderId, ctx.Saga.CustomerId,
                            ctx.Saga.Total, "CreditCard")))
                        .Then(ctx => Track(ctx)),
                    fail => fail
                        .Then(ctx => ctx.Publish(new CancelOrder(
                            ctx.Message.OrderId,
                            $"Payment failed after {ctx.Saga.RetryCount} attempts")))
                        .TransitionTo(Cancelled)
                        .Then(ctx => Track(ctx))),

            When(CancelOrderEvent)
                .TransitionTo(Cancelled)
                .Then(ctx => Track(ctx))
        );

        During(InventoryPending,
            When(InventoryReservedEvent)
                .Then(ctx => ctx.Publish(new ShipOrder(
                    ctx.Message.OrderId, ctx.Saga.CustomerId, ctx.Message.Items)))
                .TransitionTo(Shipping)
                .Then(ctx => Track(ctx)),

            When(InventoryUnavailableEvent)
                .Then(ctx => ctx.Publish(new CancelOrder(
                    ctx.Message.OrderId,
                    $"Inventory unavailable: {string.Join(", ", ctx.Message.MissingItems)}")))
                .TransitionTo(Cancelled)
                .Then(ctx => Track(ctx))
        );

        During(Shipping,
            When(OrderShippedEvent)
                .Then(ctx =>
                {
                    ctx.Saga.CompletedAt = ctx.Message.ShippedAt;
                    ctx.Publish(new SendNotification(
                        ctx.Message.OrderId, ctx.Saga.CustomerId,
                        $"Your order has shipped! Tracking: {ctx.Message.TrackingCode}", "Email"));
                })
                .TransitionTo(Completed)
                .Then(ctx => Track(ctx))
                .Finalize()
        );

        During(Cancelled,
            When(CancelOrderEvent)
        );

        SetCompletedWhenFinalized();
    }
}

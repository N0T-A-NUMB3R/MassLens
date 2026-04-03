using MassLens.Core;

namespace MassLens.Tests;

public class SagaMetricsTests
{
    [Fact]
    public void Initial_snapshot_is_empty()
    {
        var m = new SagaStateMachineMetrics("OrderSaga");
        var s = m.GetSnapshot();
        Assert.Equal("OrderSaga", s.Name);
        Assert.Empty(s.StateCounts);
        Assert.Equal(0, s.TotalTransitions);
        Assert.Equal(0, s.TotalFaulted);
        Assert.Equal(0, s.TotalCompleted);
        Assert.Empty(s.ActiveInstances);
    }

    [Fact]
    public void Transition_increments_target_state_count()
    {
        var m = new SagaStateMachineMetrics("OrderSaga");
        m.RecordTransition("corr-1", "", "Initial", false, false);
        Assert.Equal(1, m.GetSnapshot().StateCounts["Initial"]);
    }

    [Fact]
    public void Transition_decrements_previous_state_count()
    {
        var m = new SagaStateMachineMetrics("OrderSaga");
        m.RecordTransition("corr-1", "", "Initial", false, false);
        m.RecordTransition("corr-1", "Initial", "Confirmed", false, false);
        var counts = m.GetSnapshot().StateCounts;
        Assert.Equal(0, counts["Initial"]);
        Assert.Equal(1, counts["Confirmed"]);
    }

    [Fact]
    public void TotalTransitions_counts_every_call()
    {
        var m = new SagaStateMachineMetrics("OrderSaga");
        m.RecordTransition("c1", "", "A", false, false);
        m.RecordTransition("c1", "A", "B", false, false);
        m.RecordTransition("c1", "B", "C", false, false);
        Assert.Equal(3, m.GetSnapshot().TotalTransitions);
    }

    [Fact]
    public void Fault_increments_TotalFaulted()
    {
        var m = new SagaStateMachineMetrics("OrderSaga");
        m.RecordTransition("c1", "", "Faulted", true, false);
        Assert.Equal(1, m.GetSnapshot().TotalFaulted);
    }

    [Fact]
    public void Completion_increments_TotalCompleted()
    {
        var m = new SagaStateMachineMetrics("OrderSaga");
        m.RecordTransition("c1", "", "Final", false, true);
        Assert.Equal(1, m.GetSnapshot().TotalCompleted);
    }

    [Fact]
    public void Completed_instances_excluded_from_ActiveInstances()
    {
        var m = new SagaStateMachineMetrics("OrderSaga");
        m.RecordTransition("c1", "", "Active", false, false);
        m.RecordTransition("c2", "", "Final", false, true);
        var active = m.GetSnapshot().ActiveInstances;
        Assert.Single(active);
        Assert.Equal("c1", active[0].CorrelationId);
    }

    [Fact]
    public void Multiple_instances_tracked_independently()
    {
        var m = new SagaStateMachineMetrics("OrderSaga");
        m.RecordTransition("c1", "", "PaymentPending", false, false);
        m.RecordTransition("c2", "", "Shipped", false, false);
        m.RecordTransition("c3", "", "PaymentPending", false, false);
        var counts = m.GetSnapshot().StateCounts;
        Assert.Equal(2, counts["PaymentPending"]);
        Assert.Equal(1, counts["Shipped"]);
        Assert.Equal(3, m.GetSnapshot().ActiveInstances.Length);
    }

    [Fact]
    public void Transition_updates_existing_instance_state()
    {
        var m = new SagaStateMachineMetrics("OrderSaga");
        m.RecordTransition("c1", "", "Initial", false, false);
        m.RecordTransition("c1", "Initial", "Processing", false, false);
        var inst = m.GetSnapshot().ActiveInstances.Single(i => i.CorrelationId == "c1");
        Assert.Equal("Processing", inst.State);
    }

    [Fact]
    public void State_count_never_goes_below_zero()
    {
        var m = new SagaStateMachineMetrics("OrderSaga");
        m.RecordTransition("c1", "NonExistent", "Active", false, false);
        var counts = m.GetSnapshot().StateCounts;
        Assert.False(counts.ContainsKey("NonExistent") && counts["NonExistent"] < 0);
    }
}

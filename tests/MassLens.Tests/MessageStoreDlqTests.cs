using MassLens.Core;

namespace MassLens.Tests;

public class MessageStoreDlqTests
{
    [Fact]
    public void GetDlqGroup_returns_null_for_unknown_exception()
    {
        Assert.Null(MessageStore.Instance.GetDlqGroup("NonExistent.Exception"));
    }

    [Fact]
    public void Faulted_message_creates_dlq_group()
    {
        var exType = $"System.TimeoutException_{Guid.NewGuid():N}";
        var entry = new MessageEntry
        {
            MessageType      = "OrderSubmitted",
            ConsumerType     = "OrderConsumer",
            EndpointAddress  = "queue:orders",
            Direction        = MessageDirection.Faulted,
            Duration         = TimeSpan.FromMilliseconds(100),
            ExceptionType    = exType,
            ExceptionMessage = "Connection timed out",
            Timestamp        = DateTimeOffset.UtcNow
        };

        MessageStore.Instance.Write(entry);

        // give the drain channel time to process
        Thread.Sleep(100);

        var group = MessageStore.Instance.GetDlqGroup(exType);
        Assert.NotNull(group);
        Assert.Equal(exType, group.ExceptionType);
        Assert.Equal(1, group.Count);
    }

    [Fact]
    public void Multiple_faults_same_type_increment_count()
    {
        var exType = $"System.NullReferenceException_{Guid.NewGuid():N}";
        for (int i = 0; i < 3; i++)
        {
            MessageStore.Instance.Write(new MessageEntry
            {
                MessageType      = "OrderSubmitted",
                ConsumerType     = "OrderConsumer",
                EndpointAddress  = "queue:orders",
                Direction        = MessageDirection.Faulted,
                ExceptionType    = exType,
                ExceptionMessage = "Object not set",
                Timestamp        = DateTimeOffset.UtcNow
            });
        }
        Thread.Sleep(150);

        var group = MessageStore.Instance.GetDlqGroup(exType);
        Assert.NotNull(group);
        Assert.Equal(3, group.Count);
    }

    [Fact]
    public void Dlq_group_samples_capped_at_10()
    {
        var exType = $"System.OverflowException_{Guid.NewGuid():N}";
        for (int i = 0; i < 20; i++)
        {
            MessageStore.Instance.Write(new MessageEntry
            {
                MessageType      = "PaymentProcessed",
                ConsumerType     = "PaymentConsumer",
                EndpointAddress  = "queue:payments",
                Direction        = MessageDirection.Faulted,
                ExceptionType    = exType,
                ExceptionMessage = "overflow",
                Timestamp        = DateTimeOffset.UtcNow
            });
        }
        Thread.Sleep(200);

        var group = MessageStore.Instance.GetDlqGroup(exType);
        Assert.NotNull(group);
        Assert.Equal(20, group.Count);
        Assert.True(group.Samples.Count <= 10);
    }

    [Fact]
    public void GetAllDlqGroups_ordered_by_count_descending()
    {
        var ex1 = $"ExA_{Guid.NewGuid():N}";
        var ex2 = $"ExB_{Guid.NewGuid():N}";

        for (int i = 0; i < 5; i++)
            MessageStore.Instance.Write(new MessageEntry
            {
                ConsumerType  = "C", EndpointAddress = "q",
                Direction     = MessageDirection.Faulted,
                ExceptionType = ex1, Timestamp = DateTimeOffset.UtcNow
            });

        for (int i = 0; i < 2; i++)
            MessageStore.Instance.Write(new MessageEntry
            {
                ConsumerType  = "C", EndpointAddress = "q",
                Direction     = MessageDirection.Faulted,
                ExceptionType = ex2, Timestamp = DateTimeOffset.UtcNow
            });

        Thread.Sleep(150);

        var groups = MessageStore.Instance.GetAllDlqGroups().ToList();
        var idx1 = groups.FindIndex(g => g.ExceptionType == ex1);
        var idx2 = groups.FindIndex(g => g.ExceptionType == ex2);
        Assert.True(idx1 < idx2, "Higher count group should come first");
    }

    [Fact]
    public void AppendAudit_fires_OnAudit_event()
    {
        AuditEntry? received = null;
        MessageStore.Instance.OnAudit += e => received = e;

        var entry = new AuditEntry { Action = "Test", Detail = "detail", User = "tester" };
        MessageStore.Instance.AppendAudit(entry);

        Assert.NotNull(received);
        Assert.Equal("Test", received!.Action);
    }
}

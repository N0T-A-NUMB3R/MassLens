using System.Collections.Concurrent;

namespace MassLens.Core;

public sealed class SagaStateEntry
{
    public string StateMachineName { get; init; } = "";
    public string State { get; init; } = "";
    public string CorrelationId { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public bool IsFaulted { get; init; }
    public bool IsCompleted { get; init; }
}

public sealed class SagaStateMachineMetrics
{
    public string Name { get; }
    private readonly ConcurrentDictionary<string, int> _stateCounts = new();
    private readonly ConcurrentDictionary<string, SagaStateEntry> _instances = new();
    private long _totalTransitions;
    private long _totalFaulted;
    private long _totalCompleted;

    public SagaStateMachineMetrics(string name) => Name = name;

    public void RecordTransition(string correlationId, string fromState, string toState, bool isFault, bool isComplete)
    {
        Interlocked.Increment(ref _totalTransitions);

        if (!string.IsNullOrEmpty(fromState))
            _stateCounts.AddOrUpdate(fromState, 0, (_, v) => Math.Max(0, v - 1));

        _stateCounts.AddOrUpdate(toState, 1, (_, v) => v + 1);

        if (isFault) Interlocked.Increment(ref _totalFaulted);
        if (isComplete) Interlocked.Increment(ref _totalCompleted);

        _instances.AddOrUpdate(correlationId,
            _ => new SagaStateEntry
            {
                StateMachineName = Name,
                State            = toState,
                CorrelationId    = correlationId,
                CreatedAt        = DateTimeOffset.UtcNow,
                UpdatedAt        = DateTimeOffset.UtcNow,
                IsFaulted        = isFault,
                IsCompleted      = isComplete
            },
            (_, existing) => new SagaStateEntry
            {
                StateMachineName = existing.StateMachineName,
                CorrelationId    = existing.CorrelationId,
                CreatedAt        = existing.CreatedAt,
                State            = toState,
                UpdatedAt        = DateTimeOffset.UtcNow,
                IsFaulted        = isFault,
                IsCompleted      = isComplete
            });
    }

    public SagaSnapshot GetSnapshot() => new()
    {
        Name             = Name,
        StateCounts      = _stateCounts.ToDictionary(k => k.Key, k => k.Value),
        TotalTransitions = Interlocked.Read(ref _totalTransitions),
        TotalFaulted     = Interlocked.Read(ref _totalFaulted),
        TotalCompleted   = Interlocked.Read(ref _totalCompleted),
        ActiveInstances  = _instances.Values.Where(i => !i.IsCompleted).ToArray()
    };
}

public sealed class SagaSnapshot
{
    public string Name { get; init; } = "";
    public Dictionary<string, int> StateCounts { get; init; } = new();
    public long TotalTransitions { get; init; }
    public long TotalFaulted { get; init; }
    public long TotalCompleted { get; init; }
    public SagaStateEntry[] ActiveInstances { get; init; } = [];
}

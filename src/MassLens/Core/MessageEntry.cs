namespace MassLens.Core;

public enum MessageDirection { Consumed, Published, Sent, Faulted }

public sealed class MessageEntry
{
    public string MessageType { get; init; } = "";
    public string ConsumerType { get; init; } = "";
    public string EndpointAddress { get; init; } = "";
    public MessageDirection Direction { get; init; }
    public TimeSpan Duration { get; init; }
    public long SizeBytes { get; init; }
    public string? CorrelationId { get; init; }
    public string? ExceptionType { get; init; }
    public string? ExceptionMessage { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

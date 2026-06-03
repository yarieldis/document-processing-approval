namespace DocumentProcessing.Contracts.Models;

/// <summary>
/// Base event envelope for all document lifecycle events carried via Service Bus.
/// </summary>
public abstract record DocumentEvent
{
    /// <summary>Correlation id — stays constant across the entire pipeline for one document.</summary>
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("D");

    /// <summary>The document this event describes.</summary>
    public DocumentMetadata Document { get; init; } = null!;

    /// <summary>UTC timestamp when this event was raised.</summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Friendly name of the event type (set by each subclass).</summary>
    public string EventType => GetType().Name;
}

using DocumentProcessing.Contracts.Models;

namespace DocumentProcessing.Contracts.Messages;

/// <summary>
/// Raised after metadata enrichment completes (tags, entities, key-value extraction).
/// Published to topic: document-events with subject: "MetadataEnriched".
/// </summary>
public sealed record DocumentMetadataEnriched : DocumentEvent
{
    /// <summary>
    /// Number of new tags or entities that were added during enrichment.
    /// </summary>
    public int EnrichedFieldCount { get; init; }
}

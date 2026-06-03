using DocumentProcessing.Contracts.Models;

namespace DocumentProcessing.Core.Services;

/// <summary>
/// Service for enriching document metadata with extracted entities,
/// key-value pairs, and tags.
/// </summary>
public interface IMetadataEnrichmentService
{
    /// <summary>
    /// Enriches the document metadata by extracting entities, key-value pairs,
    /// dates, amounts, and other structured data from the OCR output.
    /// Returns the enriched tags and a count of new fields added.
    /// </summary>
    Task<EnrichmentResult> EnrichAsync(DocumentMetadata document, CancellationToken ct = default);
}

/// <summary>
/// Result of a metadata enrichment operation.
/// </summary>
public sealed record EnrichmentResult
{
    public Dictionary<string, string> Tags { get; init; } = new();
    public int EnrichedFieldCount => Tags.Count;
}

using DocumentProcessing.Contracts.Messages;
using DocumentProcessing.Contracts.Models;
using DocumentProcessing.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DocumentProcessing.Functions.Functions;

/// <summary>
/// Triggered when OCR content extraction completes — enriches document with metadata.
/// Subscribes to topic: document-events, subscription: enrich-metadata.
/// </summary>
public sealed class ExtractDocumentContent
{
    private readonly IMetadataEnrichmentService _enricher;
    private readonly ILogger<ExtractDocumentContent> _logger;

    public ExtractDocumentContent(IMetadataEnrichmentService enricher, ILogger<ExtractDocumentContent> logger)
    {
        _enricher = enricher;
        _logger = logger;
    }

    [Function(nameof(ExtractDocumentContent))]
    public async Task<DocumentMetadataEnriched?> Run(
        [ServiceBusTrigger(
            topicName: "document-events",
            subscriptionName: "enrich-metadata",
            Connection = "ServiceBusConnection")]
        DocumentContentExtracted extracted,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Enriching metadata for document {Id} ({PageCount} pages)",
            extracted.Document.DocumentId,
            extracted.PageCount);

        var result = await _enricher.EnrichAsync(extracted.Document, ct);

        var enriched = new DocumentMetadataEnriched
        {
            CorrelationId = extracted.CorrelationId,
            EnrichedFieldCount = result.EnrichedFieldCount,
            Document = extracted.Document with
            {
                Status = DocumentStatus.MetadataEnriched,
                Tags = result.Tags
            }
        };

        _logger.LogInformation(
            "Enriched {Fields} fields for document {Id}",
            result.EnrichedFieldCount,
            enriched.Document.DocumentId);

        return enriched;
    }
}

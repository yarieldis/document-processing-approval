using DocumentProcessing.Contracts.Messages;
using DocumentProcessing.Contracts.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DocumentProcessing.Functions.Functions;

/// <summary>
/// Triggered when metadata enrichment completes — this is the final Function stage.
/// At this point the document is ready for the approval workflow (Logic Apps).
/// Subscribes to topic: document-events, subscription: ready-for-review.
///
/// This function acts as a hand-off point: it logs readiness and could also
/// call a Logic Apps HTTP trigger or post to a queue monitored by Logic Apps.
/// </summary>
public sealed class EnrichDocumentMetadata
{
    private readonly ILogger<EnrichDocumentMetadata> _logger;

    public EnrichDocumentMetadata(ILogger<EnrichDocumentMetadata> logger)
    {
        _logger = logger;
    }

    [Function(nameof(EnrichDocumentMetadata))]
    public Task Run(
        [ServiceBusTrigger(
            topicName: "document-events",
            subscriptionName: "ready-for-review",
            Connection = "ServiceBusConnection")]
        DocumentMetadataEnriched enriched,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Document {Id} ({FileName}) is ready for review. Classification: {Label}, " +
            "Enriched fields: {Fields}, Tags: {Tags}",
            enriched.Document.DocumentId,
            enriched.Document.FileName,
            enriched.Document.Classification,
            enriched.EnrichedFieldCount,
            string.Join(", ", enriched.Document.Tags.Select(kv => $"{kv.Key}={kv.Value}")));

        // At this stage, a Logic App would pick up the event (via Service Bus trigger)
        // and begin the multi-step approval workflow:
        //   1. Notify reviewers (email / Teams)
        //   2. Present document metadata for review
        //   3. On approval → archive + compliance record
        //   4. On rejection → notify uploader with reason

        return Task.CompletedTask;
    }
}

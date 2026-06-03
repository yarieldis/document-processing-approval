using DocumentProcessing.Contracts.Messages;
using DocumentProcessing.Contracts.Models;
using DocumentProcessing.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DocumentProcessing.Functions.Functions;

/// <summary>
/// Triggered when a new document is uploaded to the pipeline.
/// Subscribes to topic: document-events, subscription: classify-document.
/// </summary>
public sealed class OnDocumentUploaded
{
    private readonly IClassificationService _classifier;
    private readonly ILogger<OnDocumentUploaded> _logger;

    public OnDocumentUploaded(IClassificationService classifier, ILogger<OnDocumentUploaded> logger)
    {
        _classifier = classifier;
        _logger = logger;
    }

    [Function(nameof(OnDocumentUploaded))]
    public async Task<DocumentClassified?> Run(
        [ServiceBusTrigger(
            topicName: "document-events",
            subscriptionName: "classify-document",
            Connection = "ServiceBusConnection")]
        DocumentUploaded uploaded,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Classifying document {Id} ({FileName}) uploaded by {User}",
            uploaded.Document.DocumentId,
            uploaded.Document.FileName,
            uploaded.Document.UploadedBy);

        var result = await _classifier.ClassifyAsync(uploaded.Document, ct);

        var classified = new DocumentClassified
        {
            CorrelationId = uploaded.CorrelationId,
            Confidence = result.Confidence,
            Document = uploaded.Document with
            {
                Status = DocumentStatus.Classified,
                Classification = result.Label
            }
        };

        _logger.LogInformation(
            "Document {Id} classified as '{Label}' ({Conf:P0} confidence)",
            classified.Document.DocumentId,
            result.Label,
            result.Confidence);

        return classified;
    }
}

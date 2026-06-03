using DocumentProcessing.Contracts.Messages;
using DocumentProcessing.Contracts.Models;
using DocumentProcessing.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DocumentProcessing.Functions.Functions;

/// <summary>
/// Triggered when a document has been classified and is ready for OCR.
/// Subscribes to topic: document-events, subscription: extract-content.
/// </summary>
public sealed class ClassifyDocument
{
    private readonly IOcrService _ocr;
    private readonly ILogger<ClassifyDocument> _logger;

    public ClassifyDocument(IOcrService ocr, ILogger<ClassifyDocument> logger)
    {
        _ocr = ocr;
        _logger = logger;
    }

    [Function(nameof(ClassifyDocument))]
    public async Task<DocumentContentExtracted?> Run(
        [ServiceBusTrigger(
            topicName: "document-events",
            subscriptionName: "extract-content",
            Connection = "ServiceBusConnection")]
        DocumentClassified classified,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Extracting content from document {Id} classified as '{Label}'",
            classified.Document.DocumentId,
            classified.Document.Classification);

        var result = await _ocr.ExtractAsync(classified.Document.BlobUri, ct);

        var extracted = new DocumentContentExtracted
        {
            CorrelationId = classified.CorrelationId,
            PageCount = result.PageCount,
            ProcessingDuration = result.ProcessingDuration,
            Document = classified.Document with
            {
                Status = DocumentStatus.ContentExtracted,
                ExtractedText = result.ExtractedText
            }
        };

        _logger.LogInformation(
            "Extracted {Pages} pages from document {Id} in {Duration}ms",
            result.PageCount,
            classified.Document.DocumentId,
            result.ProcessingDuration.TotalMilliseconds);

        return extracted;
    }
}

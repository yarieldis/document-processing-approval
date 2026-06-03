using System.Text.Json;
using DocumentProcessing.Contracts.Messages;
using DocumentProcessing.Contracts.Models;
using DocumentProcessing.Functions.Models;

namespace DocumentProcessing.Tests.Helpers;

/// <summary>
/// Factory methods for creating test data with deterministic values.
/// </summary>
public static class TestDataFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Creates a valid DocumentMetadata for testing.
    /// </summary>
    public static DocumentMetadata CreateValidMetadata(
        string fileName = "report.pdf",
        string contentType = "application/pdf",
        long fileSizeBytes = 10240,
        string blobUri = "https://storage.blob.core.windows.net/docs/report.pdf",
        string uploadedBy = "test-user@contoso.com",
        DocumentStatus status = DocumentStatus.Uploaded)
    {
        return new DocumentMetadata
        {
            DocumentId = Guid.NewGuid().ToString("D"),
            FileName = fileName,
            ContentType = contentType,
            FileSizeBytes = fileSizeBytes,
            BlobUri = blobUri,
            UploadedBy = uploadedBy,
            UploadedAt = new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero),
            Status = status
        };
    }

    /// <summary>
    /// Creates a valid IngestDocumentRequest for testing.
    /// </summary>
    public static IngestDocumentRequest CreateValidIngestRequest(
        string fileName = "report.pdf",
        string contentType = "application/pdf",
        long fileSizeBytes = 10240,
        string blobUri = "https://storage.blob.core.windows.net/docs/report.pdf")
    {
        return new IngestDocumentRequest
        {
            FileName = fileName,
            ContentType = contentType,
            FileSizeBytes = fileSizeBytes,
            BlobUri = blobUri
        };
    }

    /// <summary>
    /// Serializes an object to JSON using camelCase (matching the Functions project settings).
    /// </summary>
    public static string SerializeToJson(object obj)
        => JsonSerializer.Serialize(obj, JsonOptions);

    /// <summary>
    /// Deserializes a JSON string to the specified type.
    /// </summary>
    public static T? DeserializeFromJson<T>(string json)
        => JsonSerializer.Deserialize<T>(json, JsonOptions);

    // ── Event factories for pipeline tests ──

    public static DocumentUploaded CreateDocumentUploadedEvent(
        DocumentMetadata? metadata = null)
    {
        return new DocumentUploaded
        {
            Document = metadata ?? CreateValidMetadata()
        };
    }

    public static DocumentClassified CreateDocumentClassifiedEvent(
        DocumentMetadata? metadata = null,
        double confidence = 0.85)
    {
        var doc = metadata ?? CreateValidMetadata(status: DocumentStatus.Classified);
        return new DocumentClassified
        {
            Document = doc,
            Confidence = confidence
        };
    }

    public static DocumentContentExtracted CreateDocumentContentExtractedEvent(
        DocumentMetadata? metadata = null,
        int pageCount = 1,
        TimeSpan? processingDuration = null)
    {
        var doc = metadata ?? CreateValidMetadata(status: DocumentStatus.ContentExtracted);
        return new DocumentContentExtracted
        {
            Document = doc,
            PageCount = pageCount,
            ProcessingDuration = processingDuration ?? TimeSpan.FromMilliseconds(50)
        };
    }

    public static DocumentMetadataEnriched CreateDocumentMetadataEnrichedEvent(
        DocumentMetadata? metadata = null,
        int enrichedFieldCount = 4)
    {
        var doc = metadata ?? CreateValidMetadata(status: DocumentStatus.MetadataEnriched);
        return new DocumentMetadataEnriched
        {
            Document = doc,
            EnrichedFieldCount = enrichedFieldCount
        };
    }
}

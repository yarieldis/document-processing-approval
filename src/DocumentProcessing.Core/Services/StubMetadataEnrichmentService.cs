namespace DocumentProcessing.Core.Services;

/// <summary>
/// Stub metadata enrichment service — replace with Azure AI Language / custom NER in production.
/// </summary>
public sealed class StubMetadataEnrichmentService : IMetadataEnrichmentService
{
    public Task<EnrichmentResult> EnrichAsync(
        Contracts.Models.DocumentMetadata document,
        CancellationToken ct = default)
    {
        // TODO: Replace with Azure AI Language entity recognition, key-phrase extraction, etc.
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["document_type"] = document.Classification ?? "Unknown",
            ["file_size_kb"] = (document.FileSizeBytes / 1024.0).ToString("F1"),
            ["uploaded_date"] = document.UploadedAt.ToString("yyyy-MM-dd"),
            ["pipeline_status"] = document.Status.ToString()
        };

        return Task.FromResult(new EnrichmentResult { Tags = tags });
    }
}

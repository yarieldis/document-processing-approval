using DocumentProcessing.Contracts.Models;
using DocumentProcessing.Core.Services;

namespace DocumentProcessing.Tests.Core;

public class StubMetadataEnrichmentServiceTests
{
    private readonly StubMetadataEnrichmentService _service = new();

    [Fact]
    public async Task EnrichAsync_ReturnsFourTags()
    {
        var document = CreateTestDocument();
        var result = await _service.EnrichAsync(document);

        Assert.Equal(4, result.Tags.Count);
    }

    [Fact]
    public async Task EnrichAsync_Tag_DocumentType_MatchesClassification()
    {
        var document = CreateTestDocument(classification: "Invoice");
        var result = await _service.EnrichAsync(document);

        Assert.Equal("Invoice", result.Tags["document_type"]);
    }

    [Fact]
    public async Task EnrichAsync_Tag_FileSizeKb_ComputedCorrectly()
    {
        var document = CreateTestDocument(fileSizeBytes: 2048);
        var result = await _service.EnrichAsync(document);

        Assert.Equal("2.0", result.Tags["file_size_kb"]);
    }

    [Fact]
    public async Task EnrichAsync_Tag_FileSizeKb_RoundedToOneDecimal()
    {
        var document = CreateTestDocument(fileSizeBytes: 1536); // 1536 / 1024 = 1.5
        var result = await _service.EnrichAsync(document);

        Assert.Equal("1.5", result.Tags["file_size_kb"]);
    }

    [Fact]
    public async Task EnrichAsync_Tag_UploadedDate_FormattedCorrectly()
    {
        var document = CreateTestDocument(
            uploadedAt: new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero));
        var result = await _service.EnrichAsync(document);

        Assert.Equal("2026-06-03", result.Tags["uploaded_date"]);
    }

    [Fact]
    public async Task EnrichAsync_Tag_PipelineStatus_MatchesStatus()
    {
        var document = CreateTestDocument(status: DocumentStatus.Classified);
        var result = await _service.EnrichAsync(document);

        Assert.Equal("Classified", result.Tags["pipeline_status"]);
    }

    [Fact]
    public async Task EnrichAsync_WhenClassificationIsNull_UsesUnknown()
    {
        var document = CreateTestDocument(classification: null);
        var result = await _service.EnrichAsync(document);

        Assert.Equal("Unknown", result.Tags["document_type"]);
    }

    [Fact]
    public async Task EnrichAsync_EnrichedFieldCount_MatchesTagCount()
    {
        var document = CreateTestDocument();
        var result = await _service.EnrichAsync(document);

        Assert.Equal(result.Tags.Count, result.EnrichedFieldCount);
    }

    [Fact]
    public async Task EnrichAsync_IsDeterministic()
    {
        var document = CreateTestDocument();
        var r1 = await _service.EnrichAsync(document);
        var r2 = await _service.EnrichAsync(document);

        Assert.Equal(r1.EnrichedFieldCount, r2.EnrichedFieldCount);
        Assert.Equal(r1.Tags.Count, r2.Tags.Count);
        foreach (var kvp in r1.Tags)
            Assert.Equal(kvp.Value, r2.Tags[kvp.Key]);
    }

    // ── Helpers ────────────────────────────────────────────────────

    private static DocumentMetadata CreateTestDocument(
        string? classification = "Invoice",
        long fileSizeBytes = 2048,
        DateTimeOffset? uploadedAt = null,
        DocumentStatus status = DocumentStatus.MetadataEnriched)
    {
        return new DocumentMetadata
        {
            DocumentId = "test-doc-001",
            FileName = "invoice.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = fileSizeBytes,
            BlobUri = "https://example.com/invoice.pdf",
            UploadedBy = "test-user",
            UploadedAt = uploadedAt ?? new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            Status = status,
            Classification = classification
        };
    }
}

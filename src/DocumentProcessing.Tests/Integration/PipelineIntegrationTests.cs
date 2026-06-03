using DocumentProcessing.Contracts.Messages;
using DocumentProcessing.Contracts.Models;
using DocumentProcessing.Core.Services;
using DocumentProcessing.Functions.Functions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocumentProcessing.Tests.Integration;

/// <summary>
/// End-to-end pipeline tests using real stub implementations (no mocks).
/// Chains: OnDocumentUploaded → ClassifyDocument → ExtractDocumentContent → EnrichDocumentMetadata
/// </summary>
public class PipelineIntegrationTests
{
    private readonly OnDocumentUploaded _onUploaded;
    private readonly ClassifyDocument _classify;
    private readonly ExtractDocumentContent _extract;
    private readonly EnrichDocumentMetadata _enrich;

    public PipelineIntegrationTests()
    {
        var classifier = new StubClassificationService();
        var ocr = new StubOcrService();
        var enricher = new StubMetadataEnrichmentService();

        _onUploaded = new OnDocumentUploaded(
            classifier, NullLogger<OnDocumentUploaded>.Instance);
        _classify = new ClassifyDocument(
            ocr, NullLogger<ClassifyDocument>.Instance);
        _extract = new ExtractDocumentContent(
            enricher, NullLogger<ExtractDocumentContent>.Instance);
        _enrich = new EnrichDocumentMetadata(
            NullLogger<EnrichDocumentMetadata>.Instance);
    }

    // ── Full pipeline for each known extension ─────────────────────

    [Fact]
    public async Task FullPipeline_FromUploadedToEnriched_WithPdf()
    {
        var result = await RunPipelineAsync("invoice.pdf");

        Assert.NotNull(result);
        Assert.Equal(DocumentStatus.MetadataEnriched, result!.Document.Status);
        Assert.Equal("PDF Document", result.Document.Classification);
        Assert.Equal("[stub OCR text]", result.Document.ExtractedText);
        Assert.Equal(4, result.Document.Tags.Count);
        Assert.Equal("PDF Document", result.Document.Tags["document_type"]);
    }

    [Fact]
    public async Task FullPipeline_FromUploadedToEnriched_WithPng()
    {
        var result = await RunPipelineAsync("scan.png");

        Assert.NotNull(result);
        Assert.Equal("Image Document", result!.Document.Classification);
        Assert.Equal("[stub OCR text]", result.Document.ExtractedText);
    }

    [Fact]
    public async Task FullPipeline_FromUploadedToEnriched_WithDocx()
    {
        var result = await RunPipelineAsync("letter.docx");

        Assert.NotNull(result);
        Assert.Equal("Word Document", result!.Document.Classification);
    }

    [Fact]
    public async Task FullPipeline_FromUploadedToEnriched_WithXlsx()
    {
        var result = await RunPipelineAsync("data.xlsx");

        Assert.NotNull(result);
        Assert.Equal("Excel Spreadsheet", result!.Document.Classification);
    }

    [Fact]
    public async Task FullPipeline_FromUploadedToEnriched_WithUnknownExtension()
    {
        var result = await RunPipelineAsync("mystery.xyz");

        Assert.NotNull(result);
        Assert.Equal("Unknown", result!.Document.Classification);
        Assert.Equal("[stub OCR text]", result.Document.ExtractedText);
    }

    // ── Status transitions ─────────────────────────────────────────

    [Fact]
    public async Task StatusTransitions_AreCorrect_ThroughPipeline()
    {
        var uploaded = new DocumentUploaded
        {
            Document = new DocumentMetadata
            {
                DocumentId = "status-check-doc",
                FileName = "report.pdf",
                ContentType = "application/pdf",
                FileSizeBytes = 100,
                BlobUri = "https://example.com/report.pdf",
                UploadedBy = "test-user",
                UploadedAt = DateTimeOffset.UtcNow,
                Status = DocumentStatus.Uploaded
            }
        };

        // Stage 1: Classification
        var classified = await _onUploaded.Run(uploaded, CancellationToken.None);
        Assert.NotNull(classified);
        Assert.Equal(DocumentStatus.Classified, classified!.Document.Status);

        // Stage 2: OCR
        var extracted = await _classify.Run(classified, CancellationToken.None);
        Assert.NotNull(extracted);
        Assert.Equal(DocumentStatus.ContentExtracted, extracted!.Document.Status);

        // Stage 3: Enrichment
        var enriched = await _extract.Run(extracted, CancellationToken.None);
        Assert.NotNull(enriched);
        Assert.Equal(DocumentStatus.MetadataEnriched, enriched!.Document.Status);

        // Stage 4: Hand-off (logs, no output)
        await _enrich.Run(enriched, CancellationToken.None);
    }

    // ── CorrelationId propagation ──────────────────────────────────

    [Fact]
    public async Task CorrelationId_PropagatesThroughPipeline()
    {
        var correlationId = "e2e-trace-id-42424242";
        var uploaded = new DocumentUploaded
        {
            CorrelationId = correlationId,
            Document = new DocumentMetadata
            {
                DocumentId = "corr-check-doc",
                FileName = "report.pdf",
                ContentType = "application/pdf",
                BlobUri = "https://example.com/report.pdf",
                UploadedBy = "test-user",
                UploadedAt = DateTimeOffset.UtcNow
            }
        };

        var classified = await _onUploaded.Run(uploaded, CancellationToken.None);
        Assert.Equal(correlationId, classified!.CorrelationId);

        var extracted = await _classify.Run(classified, CancellationToken.None);
        Assert.Equal(correlationId, extracted!.CorrelationId);

        var enriched = await _extract.Run(extracted, CancellationToken.None);
        Assert.Equal(correlationId, enriched!.CorrelationId);
    }

    // ── Confidence propagation ─────────────────────────────────────

    [Fact]
    public async Task OnUploaded_ProducesClassifiedEvent_WithCorrectConfidence()
    {
        var uploaded = new DocumentUploaded
        {
            Document = new DocumentMetadata
            {
                DocumentId = "conf-doc",
                FileName = "report.pdf",
                ContentType = "application/pdf",
                BlobUri = "https://example.com/report.pdf",
                UploadedBy = "test-user",
                UploadedAt = DateTimeOffset.UtcNow
            }
        };

        var classified = await _onUploaded.Run(uploaded, CancellationToken.None);

        Assert.NotNull(classified);
        Assert.Equal(0.85, classified!.Confidence);
    }

    // ── Helpers ────────────────────────────────────────────────────

    private async Task<DocumentMetadataEnriched?> RunPipelineAsync(string fileName)
    {
        var uploaded = new DocumentUploaded
        {
            Document = new DocumentMetadata
            {
                DocumentId = Guid.NewGuid().ToString("D"),
                FileName = fileName,
                ContentType = "application/octet-stream",
                FileSizeBytes = 1000,
                BlobUri = $"https://example.com/{fileName}",
                UploadedBy = "test-user",
                UploadedAt = DateTimeOffset.UtcNow
            }
        };

        var classified = await _onUploaded.Run(uploaded, CancellationToken.None);
        Assert.NotNull(classified);

        var extracted = await _classify.Run(classified!, CancellationToken.None);
        Assert.NotNull(extracted);

        var enriched = await _extract.Run(extracted!, CancellationToken.None);
        Assert.NotNull(enriched);

        await _enrich.Run(enriched!, CancellationToken.None);

        return enriched;
    }
}

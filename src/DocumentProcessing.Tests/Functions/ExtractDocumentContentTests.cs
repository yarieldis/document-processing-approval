using DocumentProcessing.Contracts.Messages;
using DocumentProcessing.Contracts.Models;
using DocumentProcessing.Core.Services;
using DocumentProcessing.Functions.Functions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DocumentProcessing.Tests.Functions;

public class ExtractDocumentContentTests
{
    private readonly Mock<IMetadataEnrichmentService> _enricherMock = new();
    private readonly ILogger<ExtractDocumentContent> _logger = NullLogger<ExtractDocumentContent>.Instance;

    [Fact]
    public async Task Run_CallsEnricher_WithDocument()
    {
        var tags = new Dictionary<string, string> { { "key", "value" } };
        _enricherMock
            .Setup(e => e.EnrichAsync(It.IsAny<DocumentMetadata>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnrichmentResult { Tags = tags });

        var function = new ExtractDocumentContent(_enricherMock.Object, _logger);
        var extracted = new DocumentContentExtracted
        {
            Document = new DocumentMetadata
            {
                DocumentId = "doc-001",
                FileName = "report.pdf",
                Status = DocumentStatus.ContentExtracted,
                ExtractedText = "Full text here",
                Classification = "Report"
            }
        };

        await function.Run(extracted, CancellationToken.None);

        _enricherMock.Verify(
            e => e.EnrichAsync(extracted.Document, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_ReturnsMetadataEnriched_WithCorrectCorrelationId()
    {
        _enricherMock
            .Setup(e => e.EnrichAsync(It.IsAny<DocumentMetadata>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnrichmentResult { Tags = new Dictionary<string, string>() });

        var function = new ExtractDocumentContent(_enricherMock.Object, _logger);
        var extracted = new DocumentContentExtracted
        {
            CorrelationId = "corr-chain-xyz",
            Document = new DocumentMetadata
            {
                DocumentId = "doc-001",
                Status = DocumentStatus.ContentExtracted
            }
        };

        var result = await function.Run(extracted, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("corr-chain-xyz", result!.CorrelationId);
    }

    [Fact]
    public async Task Run_UpdatesStatus_ToMetadataEnriched()
    {
        _enricherMock
            .Setup(e => e.EnrichAsync(It.IsAny<DocumentMetadata>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnrichmentResult { Tags = new Dictionary<string, string>() });

        var function = new ExtractDocumentContent(_enricherMock.Object, _logger);
        var extracted = new DocumentContentExtracted
        {
            Document = new DocumentMetadata
            {
                DocumentId = "doc-001",
                Status = DocumentStatus.ContentExtracted
            }
        };

        var result = await function.Run(extracted, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(DocumentStatus.MetadataEnriched, result!.Document.Status);
    }

    [Fact]
    public async Task Run_SetsTags_FromEnrichmentResult()
    {
        var tags = new Dictionary<string, string>
        {
            { "document_type", "Invoice" },
            { "file_size_kb", "10.0" },
            { "uploaded_date", "2026-06-03" }
        };
        _enricherMock
            .Setup(e => e.EnrichAsync(It.IsAny<DocumentMetadata>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnrichmentResult { Tags = tags });

        var function = new ExtractDocumentContent(_enricherMock.Object, _logger);
        var extracted = new DocumentContentExtracted
        {
            Document = new DocumentMetadata { Status = DocumentStatus.ContentExtracted }
        };

        var result = await function.Run(extracted, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Document.Tags.Count);
        Assert.Equal("Invoice", result.Document.Tags["document_type"]);
    }

    [Fact]
    public async Task Run_SetsEnrichedFieldCount()
    {
        var tags = new Dictionary<string, string>
        {
            { "a", "1" }, { "b", "2" }, { "c", "3" }
        };
        _enricherMock
            .Setup(e => e.EnrichAsync(It.IsAny<DocumentMetadata>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnrichmentResult { Tags = tags });

        var function = new ExtractDocumentContent(_enricherMock.Object, _logger);
        var extracted = new DocumentContentExtracted
        {
            Document = new DocumentMetadata { Status = DocumentStatus.ContentExtracted }
        };

        var result = await function.Run(extracted, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(3, result!.EnrichedFieldCount);
    }
}

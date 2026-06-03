using DocumentProcessing.Contracts.Messages;
using DocumentProcessing.Contracts.Models;
using DocumentProcessing.Core.Services;
using DocumentProcessing.Functions.Functions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DocumentProcessing.Tests.Functions;

public class ClassifyDocumentTests
{
    private readonly Mock<IOcrService> _ocrMock = new();
    private readonly ILogger<ClassifyDocument> _logger = NullLogger<ClassifyDocument>.Instance;

    [Fact]
    public async Task Run_CallsOcr_WithBlobUri()
    {
        _ocrMock
            .Setup(o => o.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OcrResult
            {
                ExtractedText = "Lorem ipsum",
                PageCount = 5,
                ProcessingDuration = TimeSpan.FromMilliseconds(200)
            });

        var function = new ClassifyDocument(_ocrMock.Object, _logger);
        var classified = new DocumentClassified
        {
            CorrelationId = "corr-001",
            Document = new DocumentMetadata
            {
                DocumentId = "doc-001",
                FileName = "contract.pdf",
                BlobUri = "https://storage.example.com/contract.pdf",
                Status = DocumentStatus.Classified,
                Classification = "Contract"
            },
            Confidence = 0.85
        };

        await function.Run(classified, CancellationToken.None);

        _ocrMock.Verify(
            o => o.ExtractAsync("https://storage.example.com/contract.pdf", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_ReturnsContentExtracted_WithCorrectCorrelationId()
    {
        _ocrMock
            .Setup(o => o.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OcrResult
            {
                ExtractedText = "Text",
                PageCount = 1,
                ProcessingDuration = TimeSpan.FromMilliseconds(50)
            });

        var function = new ClassifyDocument(_ocrMock.Object, _logger);
        var classified = new DocumentClassified
        {
            CorrelationId = "pipeline-correlation-id",
            Document = new DocumentMetadata
            {
                DocumentId = "doc-001",
                BlobUri = "https://example.com/doc.pdf",
                Status = DocumentStatus.Classified
            }
        };

        var result = await function.Run(classified, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("pipeline-correlation-id", result!.CorrelationId);
    }

    [Fact]
    public async Task Run_SetsExtractedText_PageCount_AndDuration()
    {
        var duration = TimeSpan.FromSeconds(2.5);
        _ocrMock
            .Setup(o => o.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OcrResult
            {
                ExtractedText = "Sample document content",
                PageCount = 12,
                ProcessingDuration = duration
            });

        var function = new ClassifyDocument(_ocrMock.Object, _logger);
        var classified = new DocumentClassified
        {
            Document = new DocumentMetadata
            {
                DocumentId = "doc-001",
                BlobUri = "https://example.com/doc.pdf",
                Status = DocumentStatus.Classified
            }
        };

        var result = await function.Run(classified, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Sample document content", result!.Document.ExtractedText);
        Assert.Equal(12, result.PageCount);
        Assert.Equal(duration, result.ProcessingDuration);
    }

    [Fact]
    public async Task Run_UpdatesStatus_ToContentExtracted()
    {
        _ocrMock
            .Setup(o => o.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OcrResult { ExtractedText = "Text", PageCount = 1 });

        var function = new ClassifyDocument(_ocrMock.Object, _logger);
        var classified = new DocumentClassified
        {
            Document = new DocumentMetadata
            {
                DocumentId = "doc-001",
                BlobUri = "https://example.com/doc.pdf",
                Status = DocumentStatus.Classified,
                Classification = "Report"
            }
        };

        var result = await function.Run(classified, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(DocumentStatus.ContentExtracted, result!.Document.Status);
        // Classification preserved
        Assert.Equal("Report", result.Document.Classification);
    }

    [Fact]
    public async Task Run_HandlesEmptyBlobUri()
    {
        _ocrMock
            .Setup(o => o.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OcrResult { ExtractedText = "Text", PageCount = 1 });

        var function = new ClassifyDocument(_ocrMock.Object, _logger);
        var classified = new DocumentClassified
        {
            Document = new DocumentMetadata
            {
                DocumentId = "doc-001",
                BlobUri = string.Empty,
                Status = DocumentStatus.Classified
            }
        };

        var result = await function.Run(classified, CancellationToken.None);

        Assert.NotNull(result);
        _ocrMock.Verify(
            o => o.ExtractAsync(string.Empty, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

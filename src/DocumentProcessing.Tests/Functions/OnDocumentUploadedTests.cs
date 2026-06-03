using DocumentProcessing.Contracts.Messages;
using DocumentProcessing.Contracts.Models;
using DocumentProcessing.Core.Services;
using DocumentProcessing.Functions.Functions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DocumentProcessing.Tests.Functions;

public class OnDocumentUploadedTests
{
    private readonly Mock<IClassificationService> _classifierMock = new();
    private readonly ILogger<OnDocumentUploaded> _logger = NullLogger<OnDocumentUploaded>.Instance;

    [Fact]
    public async Task Run_CallsClassifyAsync_WithUploadedDocument()
    {
        _classifierMock
            .Setup(c => c.ClassifyAsync(It.IsAny<DocumentMetadata>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClassificationResult { Label = "Invoice", Confidence = 0.85 });

        var function = new OnDocumentUploaded(_classifierMock.Object, _logger);
        var uploaded = new DocumentUploaded
        {
            Document = new DocumentMetadata
            {
                FileName = "invoice.pdf",
                Status = DocumentStatus.Uploaded,
                DocumentId = "doc-001",
                BlobUri = "https://example.com/invoice.pdf"
            }
        };

        await function.Run(uploaded, CancellationToken.None);

        _classifierMock.Verify(
            c => c.ClassifyAsync(uploaded.Document, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_ReturnsDocumentClassified_WithCorrectCorrelationId()
    {
        _classifierMock
            .Setup(c => c.ClassifyAsync(It.IsAny<DocumentMetadata>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClassificationResult { Label = "Invoice", Confidence = 0.85 });

        var function = new OnDocumentUploaded(_classifierMock.Object, _logger);
        var uploaded = new DocumentUploaded
        {
            CorrelationId = "corr-pipeline-001",
            Document = new DocumentMetadata { FileName = "invoice.pdf", Status = DocumentStatus.Uploaded }
        };

        var result = await function.Run(uploaded, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("corr-pipeline-001", result!.CorrelationId);
    }

    [Fact]
    public async Task Run_ReturnsDocumentClassified_WithUpdatedStatus()
    {
        _classifierMock
            .Setup(c => c.ClassifyAsync(It.IsAny<DocumentMetadata>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClassificationResult { Label = "Invoice", Confidence = 0.85 });

        var function = new OnDocumentUploaded(_classifierMock.Object, _logger);
        var uploaded = new DocumentUploaded
        {
            Document = new DocumentMetadata { FileName = "invoice.pdf", Status = DocumentStatus.Uploaded }
        };

        var result = await function.Run(uploaded, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(DocumentStatus.Classified, result!.Document.Status);
    }

    [Fact]
    public async Task Run_SetsClassificationAndConfidence_FromResult()
    {
        _classifierMock
            .Setup(c => c.ClassifyAsync(It.IsAny<DocumentMetadata>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClassificationResult { Label = "Report", Confidence = 0.72 });

        var function = new OnDocumentUploaded(_classifierMock.Object, _logger);
        var uploaded = new DocumentUploaded
        {
            Document = new DocumentMetadata { FileName = "annual.pdf", Status = DocumentStatus.Uploaded }
        };

        var result = await function.Run(uploaded, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Report", result!.Document.Classification);
        Assert.Equal(0.72, result.Confidence);
    }

    [Fact]
    public async Task Run_PreservesExistingDocumentProperties()
    {
        _classifierMock
            .Setup(c => c.ClassifyAsync(It.IsAny<DocumentMetadata>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClassificationResult { Label = "Image Document", Confidence = 0.85 });

        var function = new OnDocumentUploaded(_classifierMock.Object, _logger);
        var uploaded = new DocumentUploaded
        {
            Document = new DocumentMetadata
            {
                DocumentId = "original-id",
                FileName = "photo.png",
                ContentType = "image/png",
                FileSizeBytes = 51200,
                BlobUri = "https://example.com/photo.png",
                UploadedBy = "alice@contoso.com",
                Status = DocumentStatus.Uploaded
            }
        };

        var result = await function.Run(uploaded, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("original-id", result!.Document.DocumentId);
        Assert.Equal("photo.png", result.Document.FileName);
        Assert.Equal("image/png", result.Document.ContentType);
        Assert.Equal(51200, result.Document.FileSizeBytes);
        Assert.Equal("https://example.com/photo.png", result.Document.BlobUri);
        Assert.Equal("alice@contoso.com", result.Document.UploadedBy);
    }
}

using DocumentProcessing.Contracts.Models;
using DocumentProcessing.Core.Services;

namespace DocumentProcessing.Tests.Core;

public class StubClassificationServiceTests
{
    private readonly StubClassificationService _service = new();

    [Theory]
    [InlineData("report.pdf", "PDF Document")]
    [InlineData("REPORT.PDF", "PDF Document")]
    [InlineData("Report.Pdf", "PDF Document")]
    [InlineData("image.png", "Image Document")]
    [InlineData("photo.jpg", "Image Document")]
    [InlineData("scan.jpeg", "Image Document")]
    [InlineData("letter.docx", "Word Document")]
    [InlineData("spreadsheet.xlsx", "Excel Spreadsheet")]
    public async Task ClassifyAsync_WithKnownExtension_ReturnsCorrectLabel(
        string fileName, string expectedLabel)
    {
        var document = new DocumentMetadata { FileName = fileName };
        var result = await _service.ClassifyAsync(document);

        Assert.Equal(expectedLabel, result.Label);
    }

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("data.csv")]
    [InlineData("archive.zip")]
    [InlineData("file.html")]
    public async Task ClassifyAsync_WithUnknownExtension_ReturnsUnknown(string fileName)
    {
        var document = new DocumentMetadata { FileName = fileName };
        var result = await _service.ClassifyAsync(document);

        Assert.Equal("Unknown", result.Label);
    }

    [Fact]
    public async Task ClassifyAsync_WithNoExtension_ReturnsUnknown()
    {
        var document = new DocumentMetadata { FileName = "Makefile" };
        var result = await _service.ClassifyAsync(document);

        Assert.Equal("Unknown", result.Label);
    }

    [Fact]
    public async Task ClassifyAsync_WithEmptyFileName_ReturnsUnknown()
    {
        var document = new DocumentMetadata { FileName = string.Empty };
        var result = await _service.ClassifyAsync(document);

        Assert.Equal("Unknown", result.Label);
    }

    [Fact]
    public async Task ClassifyAsync_Always_Returns085Confidence()
    {
        var document = new DocumentMetadata { FileName = "anything.pdf" };
        var result = await _service.ClassifyAsync(document);

        Assert.Equal(0.85, result.Confidence);
    }

    [Fact]
    public async Task ClassifyAsync_OnlyUsesFileName_NotContentType()
    {
        var document = new DocumentMetadata
        {
            FileName = "report.docx",
            ContentType = "application/pdf" // misleading content type
        };
        var result = await _service.ClassifyAsync(document);

        // File extension (.docx) wins, content type is ignored
        Assert.Equal("Word Document", result.Label);
    }

    [Fact]
    public async Task ClassifyAsync_IsDeterministic()
    {
        var doc = new DocumentMetadata { FileName = "report.pdf" };
        var r1 = await _service.ClassifyAsync(doc);
        var r2 = await _service.ClassifyAsync(doc);

        Assert.Equal(r1.Label, r2.Label);
        Assert.Equal(r1.Confidence, r2.Confidence);
    }

    [Fact]
    public async Task ClassifyAsync_FileWithMultipleDots_UsesLastExtension()
    {
        var document = new DocumentMetadata { FileName = "report.backup.pdf" };
        var result = await _service.ClassifyAsync(document);

        Assert.Equal("PDF Document", result.Label);
    }
}

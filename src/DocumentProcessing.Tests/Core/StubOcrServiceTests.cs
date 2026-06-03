using DocumentProcessing.Core.Services;

namespace DocumentProcessing.Tests.Core;

public class StubOcrServiceTests
{
    private readonly StubOcrService _service = new();

    [Fact]
    public async Task ExtractAsync_ReturnsStubText()
    {
        var result = await _service.ExtractAsync("https://example.com/blob/test.pdf");
        Assert.Equal("[stub OCR text]", result.ExtractedText);
    }

    [Fact]
    public async Task ExtractAsync_ReturnsOnePage()
    {
        var result = await _service.ExtractAsync("https://example.com/blob/test.pdf");
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public async Task ExtractAsync_ReturnsFiftyMsDuration()
    {
        var result = await _service.ExtractAsync("https://example.com/blob/test.pdf");
        Assert.Equal(TimeSpan.FromMilliseconds(50), result.ProcessingDuration);
    }

    [Fact]
    public async Task ExtractAsync_IgnoresBlobUri()
    {
        var r1 = await _service.ExtractAsync("https://example.com/blob/a.pdf");
        var r2 = await _service.ExtractAsync("https://different.url/b.docx");
        var r3 = await _service.ExtractAsync(string.Empty);

        Assert.Equal(r1.ExtractedText, r2.ExtractedText);
        Assert.Equal(r1.PageCount, r2.PageCount);
        Assert.Equal(r1.ProcessingDuration, r2.ProcessingDuration);
        Assert.Equal(r1.ExtractedText, r3.ExtractedText);
    }

    [Fact]
    public async Task ExtractAsync_IsDeterministic()
    {
        var r1 = await _service.ExtractAsync("https://example.com/test.pdf");
        var r2 = await _service.ExtractAsync("https://example.com/test.pdf");

        Assert.Equal(r1.ExtractedText, r2.ExtractedText);
        Assert.Equal(r1.PageCount, r2.PageCount);
        Assert.Equal(r1.ProcessingDuration, r2.ProcessingDuration);
    }
}

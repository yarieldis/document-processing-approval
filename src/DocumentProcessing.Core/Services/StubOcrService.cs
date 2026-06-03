using DocumentProcessing.Core.Services;

namespace DocumentProcessing.Core.Services;

/// <summary>
/// Stub OCR service — replace with Azure AI Document Intelligence (Form Recognizer) in production.
/// </summary>
public sealed class StubOcrService : IOcrService
{
    public Task<OcrResult> ExtractAsync(string blobUri, CancellationToken ct = default)
    {
        // TODO: Replace with Azure.AI.FormRecognizer DocumentAnalysisClient
        return Task.FromResult(new OcrResult
        {
            ExtractedText = "[stub OCR text]",
            PageCount = 1,
            ProcessingDuration = TimeSpan.FromMilliseconds(50)
        });
    }
}

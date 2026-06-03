using DocumentProcessing.Contracts.Models;

namespace DocumentProcessing.Core.Services;

/// <summary>
/// Service for extracting text content from documents using OCR.
/// </summary>
public interface IOcrService
{
    /// <summary>
    /// Extracts text content from a document stored at the given blob URI.
    /// Returns the extracted text and page count.
    /// </summary>
    Task<OcrResult> ExtractAsync(string blobUri, CancellationToken ct = default);
}

/// <summary>
/// Result of an OCR extraction operation.
/// </summary>
public sealed record OcrResult
{
    public string ExtractedText { get; init; } = string.Empty;
    public int PageCount { get; init; }
    public TimeSpan ProcessingDuration { get; init; }
}

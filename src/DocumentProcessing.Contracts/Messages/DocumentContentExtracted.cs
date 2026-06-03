using DocumentProcessing.Contracts.Models;

namespace DocumentProcessing.Contracts.Messages;

/// <summary>
/// Raised after OCR text extraction completes.
/// Published to topic: document-events with subject: "ContentExtracted".
/// </summary>
public sealed record DocumentContentExtracted : DocumentEvent
{
    /// <summary>
    /// Number of pages processed.
    /// </summary>
    public int PageCount { get; init; }

    /// <summary>
    /// How long the OCR operation took.
    /// </summary>
    public TimeSpan ProcessingDuration { get; init; }
}

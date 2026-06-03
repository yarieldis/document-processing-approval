using DocumentProcessing.Contracts.Models;

namespace DocumentProcessing.Contracts.Messages;

/// <summary>
/// Raised after the document type has been classified.
/// Published to topic: document-events with subject: "Classified".
/// </summary>
public sealed record DocumentClassified : DocumentEvent
{
    /// <summary>
    /// Confidence score for the classification (0.0 – 1.0).
    /// </summary>
    public double Confidence { get; init; }
}

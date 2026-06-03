using DocumentProcessing.Contracts.Models;

namespace DocumentProcessing.Contracts.Messages;

/// <summary>
/// Raised when the document is approved in the review workflow.
/// Published to topic: document-events with subject: "Approved".
/// </summary>
public sealed record DocumentApproved : DocumentEvent
{
    /// <summary>
    /// Final archival path or compliance record reference.
    /// </summary>
    public string? ArchivePath { get; init; }
}

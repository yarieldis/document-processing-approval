using DocumentProcessing.Contracts.Models;

namespace DocumentProcessing.Contracts.Messages;

/// <summary>
/// Raised when the document is rejected during the approval workflow.
/// Published to topic: document-events with subject: "Rejected".
/// </summary>
public sealed record DocumentRejected : DocumentEvent
{
    /// <summary>
    /// Reason the document was rejected.
    /// </summary>
    public string? RejectionReason { get; init; }
}

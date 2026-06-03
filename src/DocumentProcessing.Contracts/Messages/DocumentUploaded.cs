using DocumentProcessing.Contracts.Models;

namespace DocumentProcessing.Contracts.Messages;

/// <summary>
/// Raised when a new document is uploaded to blob storage and enters the pipeline.
/// Published to topic: document-events with subject: "Uploaded".
/// </summary>
public sealed record DocumentUploaded : DocumentEvent
{
    public DocumentUploaded()
    {
        Document = Document.Status is DocumentStatus.Uploaded
            ? Document
            : Document with { Status = DocumentStatus.Uploaded };
    }
}

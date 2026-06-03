using DocumentProcessing.Contracts.Models;

namespace DocumentProcessing.Contracts.Messages;

/// <summary>
/// Raised when a new document is uploaded to blob storage and enters the pipeline.
/// Published to topic: document-events with subject: "Uploaded".
/// The caller is responsible for ensuring Document.Status is set to DocumentStatus.Uploaded.
/// </summary>
public sealed record DocumentUploaded : DocumentEvent;

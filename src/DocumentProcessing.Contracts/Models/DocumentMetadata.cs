namespace DocumentProcessing.Contracts.Models;

/// <summary>
/// Common metadata attached to every document that passes through the pipeline.
/// </summary>
public sealed record DocumentMetadata
{
    /// <summary>Unique identifier for the document.</summary>
    public string DocumentId { get; init; } = Guid.NewGuid().ToString("D");

    /// <summary>Original file name as provided during upload.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>MIME type of the document (e.g. application/pdf, image/png).</summary>
    public string ContentType { get; init; } = string.Empty;

    /// <summary>Size of the document in bytes.</summary>
    public long FileSizeBytes { get; init; }

    /// <summary>URI pointing to the stored blob.</summary>
    public string BlobUri { get; init; } = string.Empty;

    /// <summary>Who uploaded the document.</summary>
    public string UploadedBy { get; init; } = string.Empty;

    /// <summary>When the document entered the pipeline (UTC).</summary>
    public DateTimeOffset UploadedAt { get; init; }

    /// <summary>Current lifecycle status.</summary>
    public DocumentStatus Status { get; init; } = DocumentStatus.Uploaded;

    /// <summary>Classification label assigned after analysis (e.g. Invoice, Contract, Report).</summary>
    public string? Classification { get; init; }

    /// <summary>Extracted text content from OCR.</summary>
    public string? ExtractedText { get; init; }

    /// <summary>Arbitrary key/value tags added during enrichment.</summary>
    public Dictionary<string, string> Tags { get; init; } = new();

    /// <summary>Who approved or rejected the document (set at final stage).</summary>
    public string? ReviewedBy { get; init; }

    /// <summary>Reviewer comments.</summary>
    public string? ReviewNotes { get; init; }
}

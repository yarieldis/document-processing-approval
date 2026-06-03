namespace DocumentProcessing.Functions.Models;

/// <summary>
/// Client-supplied document metadata for ingestion.
/// UploadedBy is deliberately absent — it is derived from the authenticated token.
/// </summary>
public sealed record IngestDocumentRequest
{
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public string BlobUri { get; init; } = string.Empty;
}

using DocumentProcessing.Contracts.Models;

namespace DocumentProcessing.Core.Services;

/// <summary>
/// Service for classifying document types (Invoice, Contract, Report, etc.).
/// </summary>
public interface IClassificationService
{
    /// <summary>
    /// Classifies a document based on its metadata and optionally its extracted text.
    /// Returns the classification label and a confidence score.
    /// </summary>
    Task<ClassificationResult> ClassifyAsync(DocumentMetadata document, CancellationToken ct = default);
}

/// <summary>
/// Result of a document classification operation.
/// </summary>
public sealed record ClassificationResult
{
    public string Label { get; init; } = "Unknown";
    public double Confidence { get; init; }
}

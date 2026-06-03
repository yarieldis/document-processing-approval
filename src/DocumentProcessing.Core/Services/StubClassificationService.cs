namespace DocumentProcessing.Core.Services;

/// <summary>
/// Stub classification service — replace with a model-driven classifier in production.
/// </summary>
public sealed class StubClassificationService : IClassificationService
{
    public Task<ClassificationResult> ClassifyAsync(
        Contracts.Models.DocumentMetadata document,
        CancellationToken ct = default)
    {
        // TODO: Replace with a real classifier (Azure AI Document Intelligence custom model, etc.)
        var label = Path.GetExtension(document.FileName)?.ToLowerInvariant() switch
        {
            ".pdf" => "PDF Document",
            ".png" or ".jpg" or ".jpeg" => "Image Document",
            ".docx" => "Word Document",
            ".xlsx" => "Excel Spreadsheet",
            _ => "Unknown"
        };

        return Task.FromResult(new ClassificationResult
        {
            Label = label,
            Confidence = 0.85
        });
    }
}

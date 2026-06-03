namespace DocumentProcessing.Contracts.Models;

/// <summary>
/// The lifecycle status of a document moving through the pipeline.
/// </summary>
public enum DocumentStatus
{
    /// <summary>Initial state — raw document received, waiting to be classified.</summary>
    Uploaded = 1,

    /// <summary>Document type has been identified.</summary>
    Classified = 2,

    /// <summary>Text content has been extracted via OCR.</summary>
    ContentExtracted = 3,

    /// <summary>Metadata has been augmented/enriched.</summary>
    MetadataEnriched = 4,

    /// <summary>Final — approved through the workflow.</summary>
    Approved = 5,

    /// <summary>Final — rejected at some stage in the workflow.</summary>
    Rejected = 6
}

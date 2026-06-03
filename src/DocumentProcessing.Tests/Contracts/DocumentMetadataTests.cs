using DocumentProcessing.Contracts.Models;

namespace DocumentProcessing.Tests.Contracts;

public class DocumentMetadataTests
{
    [Fact]
    public void Default_Constructor_SetsDocumentIdToNonEmptyGuid()
    {
        var metadata = new DocumentMetadata();
        Assert.False(string.IsNullOrEmpty(metadata.DocumentId));
        Assert.True(Guid.TryParse(metadata.DocumentId, out _));
    }

    [Fact]
    public void Default_Constructor_SetsStatusToUploaded()
    {
        var metadata = new DocumentMetadata();
        Assert.Equal(DocumentStatus.Uploaded, metadata.Status);
    }

    [Fact]
    public void Default_Constructor_TagsIsEmptyDictionary()
    {
        var metadata = new DocumentMetadata();
        Assert.NotNull(metadata.Tags);
        Assert.Empty(metadata.Tags);
    }

    [Fact]
    public void Default_Constructor_NullableFieldsAreNull()
    {
        var metadata = new DocumentMetadata();
        Assert.Null(metadata.Classification);
        Assert.Null(metadata.ExtractedText);
        Assert.Null(metadata.ReviewedBy);
        Assert.Null(metadata.ReviewNotes);
    }

    [Fact]
    public void Default_Constructor_StringFieldsAreEmpty()
    {
        var metadata = new DocumentMetadata();
        Assert.Equal(string.Empty, metadata.FileName);
        Assert.Equal(string.Empty, metadata.ContentType);
        Assert.Equal(string.Empty, metadata.BlobUri);
        Assert.Equal(string.Empty, metadata.UploadedBy);
    }

    [Fact]
    public void Default_Constructor_FileSizeBytesIsZero()
    {
        var metadata = new DocumentMetadata();
        Assert.Equal(0, metadata.FileSizeBytes);
    }

    [Fact]
    public void InitProperties_ValuesRoundTrip()
    {
        var metadata = new DocumentMetadata
        {
            DocumentId = "abc-123",
            FileName = "invoice.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 2048,
            BlobUri = "https://example.com/invoice.pdf",
            UploadedBy = "alice@contoso.com",
            UploadedAt = new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero),
            Status = DocumentStatus.Approved,
            Classification = "Invoice",
            ExtractedText = "Sample text",
            Tags = new Dictionary<string, string> { { "key", "value" } },
            ReviewedBy = "bob@contoso.com",
            ReviewNotes = "Looks good"
        };

        Assert.Equal("abc-123", metadata.DocumentId);
        Assert.Equal("invoice.pdf", metadata.FileName);
        Assert.Equal("application/pdf", metadata.ContentType);
        Assert.Equal(2048, metadata.FileSizeBytes);
        Assert.Equal("https://example.com/invoice.pdf", metadata.BlobUri);
        Assert.Equal("alice@contoso.com", metadata.UploadedBy);
        Assert.Equal(new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero), metadata.UploadedAt);
        Assert.Equal(DocumentStatus.Approved, metadata.Status);
        Assert.Equal("Invoice", metadata.Classification);
        Assert.Equal("Sample text", metadata.ExtractedText);
        Assert.Single(metadata.Tags);
        Assert.Equal("bob@contoso.com", metadata.ReviewedBy);
        Assert.Equal("Looks good", metadata.ReviewNotes);
    }

    [Fact]
    public void With_Expression_CreatesNewInstance()
    {
        var original = new DocumentMetadata
        {
            FileName = "original.pdf",
            ContentType = "application/pdf"
        };

        var modified = original with { FileName = "modified.pdf" };

        Assert.NotSame(original, modified);
        Assert.Equal("original.pdf", original.FileName);
        Assert.Equal("modified.pdf", modified.FileName);
        Assert.Equal(original.ContentType, modified.ContentType);
        Assert.Equal(original.DocumentId, modified.DocumentId);
    }

    [Fact]
    public void With_Expression_StatusTransition()
    {
        var uploaded = new DocumentMetadata { Status = DocumentStatus.Uploaded };
        var classified = uploaded with { Status = DocumentStatus.Classified, Classification = "Invoice" };

        Assert.Equal(DocumentStatus.Uploaded, uploaded.Status);
        Assert.Equal(DocumentStatus.Classified, classified.Status);
        Assert.Equal("Invoice", classified.Classification);
        Assert.Null(uploaded.Classification);
    }

    [Fact]
    public void TwoRecords_WithSameScalarProperties_AreEqual()
    {
        var a = new DocumentMetadata
        {
            DocumentId = "id",
            FileName = "file.pdf",
            FileSizeBytes = 100,
            Tags = new Dictionary<string, string> { { "k", "v" } }
        };
        var b = new DocumentMetadata
        {
            DocumentId = "id",
            FileName = "file.pdf",
            FileSizeBytes = 100,
            Tags = a.Tags // Same reference — Dictionary uses reference equality
        };

        // Records use value equality, but Dictionary uses reference equality.
        // When Tags share the same reference, the records are equal.
        Assert.Equal(a, b);

        // With different Dictionary instances, they are NOT equal:
        var c = a with { Tags = new Dictionary<string, string> { { "k", "v" } } };
        Assert.NotEqual(a, c);
    }
}

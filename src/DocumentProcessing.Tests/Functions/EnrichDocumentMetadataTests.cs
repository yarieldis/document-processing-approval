using DocumentProcessing.Contracts.Messages;
using DocumentProcessing.Contracts.Models;
using DocumentProcessing.Functions.Functions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DocumentProcessing.Tests.Functions;

public class EnrichDocumentMetadataTests
{
    [Fact]
    public async Task Run_ReturnsCompletedTask()
    {
        var logger = NullLogger<EnrichDocumentMetadata>.Instance;
        var function = new EnrichDocumentMetadata(logger);
        var enriched = new DocumentMetadataEnriched
        {
            CorrelationId = "corr-001",
            Document = new DocumentMetadata
            {
                DocumentId = "doc-001",
                FileName = "report.pdf",
                Classification = "Report",
                Status = DocumentStatus.MetadataEnriched,
                Tags = new Dictionary<string, string> { { "key", "value" } }
            },
            EnrichedFieldCount = 1
        };

        await function.Run(enriched, CancellationToken.None);

        // Returning Task.CompletedTask — no exception thrown
        // If it completes without throwing, the test passes
    }

    [Fact]
    public async Task Run_Completes_WhenTagsAreEmpty()
    {
        var logger = NullLogger<EnrichDocumentMetadata>.Instance;
        var function = new EnrichDocumentMetadata(logger);
        var enriched = new DocumentMetadataEnriched
        {
            Document = new DocumentMetadata
            {
                DocumentId = "doc-001",
                Status = DocumentStatus.MetadataEnriched,
                Tags = new Dictionary<string, string>()
            }
        };

        // Should not throw
        await function.Run(enriched, CancellationToken.None);
    }

    [Fact]
    public async Task Run_Completes_WhenClassificationIsNull()
    {
        var logger = NullLogger<EnrichDocumentMetadata>.Instance;
        var function = new EnrichDocumentMetadata(logger);
        var enriched = new DocumentMetadataEnriched
        {
            Document = new DocumentMetadata
            {
                DocumentId = "doc-001",
                Classification = null,
                Status = DocumentStatus.MetadataEnriched,
                Tags = new Dictionary<string, string> { { "doc_type", "Unknown" } }
            }
        };

        // Should not throw — log statements handle null Classification
        await function.Run(enriched, CancellationToken.None);
    }
}

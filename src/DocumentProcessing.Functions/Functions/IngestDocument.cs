using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using DocumentProcessing.Contracts.Messages;
using DocumentProcessing.Contracts.Models;
using DocumentProcessing.Functions.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace DocumentProcessing.Functions.Functions;

public sealed class IngestDocument
{
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ILogger<IngestDocument> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public IngestDocument(ServiceBusClient serviceBusClient, ILogger<IngestDocument> logger)
    {
        _serviceBusClient = serviceBusClient;
        _logger = logger;
    }

    [Function(nameof(IngestDocument))]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "documents")]
        HttpRequestData req,
        FunctionContext context,
        CancellationToken ct)
    {
        // 1. Verify authenticated identity exists (set by AuthenticationMiddleware)
        if (!context.Items.TryGetValue("AuthPrincipal", out var principalObj) ||
            principalObj is not ClaimsPrincipal principal)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteStringAsync("Authentication required.");
            return unauthorized;
        }

        // 2. Deserialize request body
        var requestBody = await req.ReadAsStringAsync() ?? string.Empty;
        IngestDocumentRequest? ingestRequest;
        try
        {
            ingestRequest = JsonSerializer.Deserialize<IngestDocumentRequest>(
                requestBody, JsonOptions);
        }
        catch (JsonException ex)
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync($"Invalid JSON: {ex.Message}");
            return badRequest;
        }

        // 3. Validate required fields
        if (ingestRequest is null ||
            string.IsNullOrWhiteSpace(ingestRequest.FileName) ||
            string.IsNullOrWhiteSpace(ingestRequest.ContentType))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync(
                "Request body must include non-empty fileName and contentType.");
            return badRequest;
        }

        // 4. Extract identity from the authenticated principal
        var identity = ExtractIdentity(principal);

        // 5. Build the document metadata (UploadedBy comes from the token, not the request)
        var metadata = new DocumentMetadata
        {
            DocumentId = Guid.NewGuid().ToString("D"),
            FileName = ingestRequest.FileName,
            ContentType = ingestRequest.ContentType,
            FileSizeBytes = ingestRequest.FileSizeBytes,
            BlobUri = ingestRequest.BlobUri,
            UploadedBy = identity,
            UploadedAt = DateTimeOffset.UtcNow,
            Status = DocumentStatus.Uploaded
        };

        // 6. Create the DocumentUploaded event
        var documentEvent = new DocumentUploaded { Document = metadata };

        _logger.LogInformation(
            "Ingesting document {DocumentId} ({FileName}) uploaded by {UploadedBy}",
            metadata.DocumentId, metadata.FileName, metadata.UploadedBy);

        // 7. Publish to Service Bus topic
        var sender = _serviceBusClient.CreateSender("document-events");
        try
        {
            var serialized = JsonSerializer.Serialize(documentEvent, JsonOptions);
            var message = new ServiceBusMessage(serialized)
            {
                ContentType = "application/json",
                CorrelationId = documentEvent.CorrelationId
            };
            message.ApplicationProperties.Add("EventType", documentEvent.EventType);

            await sender.SendMessageAsync(message, ct);

            _logger.LogInformation(
                "Document {DocumentId} published to document-events (CorrelationId: {CorrelationId})",
                metadata.DocumentId, documentEvent.CorrelationId);
        }
        finally
        {
            // Dispose sender; do NOT dispose ServiceBusClient (it's a singleton)
            await sender.DisposeAsync();
        }

        // 8. Return 201 Created with document reference
        var response = req.CreateResponse(HttpStatusCode.Created);
        response.Headers.Add("Location", $"/api/documents/{metadata.DocumentId}");

        var responseBody = new
        {
            documentId = metadata.DocumentId,
            status = metadata.Status.ToString(),
            correlationId = documentEvent.CorrelationId,
            location = $"/api/documents/{metadata.DocumentId}"
        };

        var responseJson = JsonSerializer.Serialize(responseBody, JsonOptions);
        await response.WriteStringAsync(responseJson);

        return response;
    }

    /// <summary>
    /// Extracts the user identity from the authenticated ClaimsPrincipal.
    /// Priority: UPN → preferred_username → name → sub.
    /// </summary>
    internal static string ExtractIdentity(ClaimsPrincipal principal)
    {
        return principal.FindFirst(ClaimTypes.Upn)?.Value
            ?? principal.FindFirst("preferred_username")?.Value
            ?? principal.FindFirst(ClaimTypes.Name)?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "unknown";
    }
}

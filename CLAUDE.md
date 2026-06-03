# CLAUDE.md — Document Processing & Approval Pipeline

This file provides guidance to Claude Code when working with the **Document Processing & Approval Pipeline** repository.

## Repository Overview

**Purpose**: A serverless document processing backend built on .NET 10 and Azure. Documents enter through blob storage, flow through an automated pipeline (classification → OCR → metadata enrichment), and then enter a human approval workflow orchestrated by Logic Apps.

**Status**: Active development — scaffolded, builds clean, stubs in place.

**Technology Stack**:
- .NET 10 (net10.0) with C# 14
- Azure Functions v4 (isolated worker model, `dotnet-isolated`)
- Azure Service Bus (topics + subscriptions for pub/sub messaging)
- Azure Logic Apps (approval workflows — configured in Azure, not in this repo)
- Azure AI Document Intelligence / Azure AI Language (production; stubs in dev)

## Project Structure

```
document-processing-approval/
├── DocumentProcessing.sln                          # Solution file (3 projects)
├── README.md                                       # Project overview & quick start
├── CLAUDE.md                                       # This file
├── AGENTS.md                                       # AI agent instructions
├── analysis.txt                                    # Technology-fit analysis (Service Bus, Functions, Logic Apps)
├── docs/
│   ├── project-deep-dive.md                        # Architecture, lifecycle, and design rationale
│   └── step-by-step-scaffold.md                    # Step-by-step to recreate this project from scratch
└── src/
    ├── DocumentProcessing.Contracts/               # Shared types — referenced by every project
    │   ├── DocumentProcessing.Contracts.csproj
    │   ├── Models/
    │   │   ├── DocumentStatus.cs                   # Enum: Uploaded → Classified → … → Approved/Rejected
    │   │   ├── DocumentMetadata.cs                 # Main data record (immutable, uses `with` expressions)
    │   │   └── DocumentEvent.cs                    # Base event record (CorrelationId + Document + timestamp)
    │   └── Messages/
    │       ├── DocumentUploaded.cs                 # Entry-point event
    │       ├── DocumentClassified.cs               # + Confidence (double)
    │       ├── DocumentContentExtracted.cs          # + PageCount, ProcessingDuration
    │       ├── DocumentMetadataEnriched.cs          # + EnrichedFieldCount
    │       ├── DocumentApproved.cs                 # Terminal — + ArchivePath
    │       └── DocumentRejected.cs                 # Terminal — + RejectionReason
    ├── DocumentProcessing.Core/                    # Business logic layer (interfaces + stubs)
    │   ├── DocumentProcessing.Core.csproj
    │   └── Services/
    │       ├── IClassificationService.cs           # Interface: classify document type
    │       ├── IOcrService.cs                      # Interface: extract text from blob
    │       ├── IMetadataEnrichmentService.cs       # Interface: extract entities/tags
    │       ├── StubClassificationService.cs        # Stub: classifies by file extension
    │       ├── StubOcrService.cs                   # Stub: returns placeholder OCR text
    │       └── StubMetadataEnrichmentService.cs    # Stub: returns basic tags
    └── DocumentProcessing.Functions/               # Compute host — 5 Azure Functions + auth middleware
        ├── DocumentProcessing.Functions.csproj      # Worker SDK + Service Bus + HTTP + JWT extensions
        ├── Program.cs                              # Host builder + DI registration + middleware
        ├── host.json                               # Logging & runtime config
        ├── local.settings.json                     # Local dev connection strings + auth bypass
        ├── Configuration/
        │   └── AuthOptions.cs                      # Auth config POCO (Bypass, TenantId, ClientId, Issuer)
        ├── Models/
        │   └── IngestDocumentRequest.cs            # HTTP request body (no UploadedBy — from token)
        ├── Middleware/
        │   └── AuthenticationMiddleware.cs         # JWT validation + role check + dev bypass mode
        └── Functions/
            ├── IngestDocument.cs                   # [POST] /api/documents — HTTP entry point with auth
            ├── OnDocumentUploaded.cs               # Trigger: classify-document sub → Classifies → DocumentClassified
            ├── ClassifyDocument.cs                 # Trigger: extract-content sub → Runs OCR → DocumentContentExtracted
            ├── ExtractDocumentContent.cs           # Trigger: enrich-metadata sub → Enriches → DocumentMetadataEnriched
            └── EnrichDocumentMetadata.cs           # Trigger: ready-for-review sub → Logs, hands off to Logic Apps (void)
    └── DocumentProcessing.Tests/                   # Test project — 97 tests across all layers
        ├── DocumentProcessing.Tests.csproj         # xUnit + Moq + coverlet
        ├── Helpers/
        ├── Contracts/
        ├── Core/
        ├── Functions/
        ├── Middleware/
        └── Integration/
```

## Quick Start

### Prerequisites
- .NET 10 SDK (10.0.300+)
- Azure Functions Core Tools v4 (`npm install -g azure-functions-core-tools@4`)
- (Optional) Azurite or Azure Storage emulator for local blob development

### Restore and Build
```bash
dotnet restore DocumentProcessing.sln
dotnet build DocumentProcessing.sln --no-restore
```

Expected: 0 warnings, 0 errors.

### Run Tests
```bash
dotnet test DocumentProcessing.sln
```

97 tests across all layers: contracts models, core stubs, Service Bus functions, HTTP trigger logic, auth middleware, and end-to-end pipeline integration.

### Run Functions Locally
```bash
cd src/DocumentProcessing.Functions
func start
```

> Without a reachable Service Bus, the Functions host starts but triggers won't receive messages. Update `ServiceBusConnection` in `local.settings.json` to point to a real Azure Service Bus namespace or a local emulator.

## Architecture Patterns

### 1. Pub/Sub via Service Bus Topic

A single topic (`document-events`) with multiple subscriptions. Each function subscribes to one subscription and receives only the event type it cares about. New subscribers (audit log, real-time dashboard, metrics) can be added without changing existing functions.

**Why topics, not queues?** Queues are competing-consumer (one message → one receiver). Topics are pub/sub (one message → many receivers). Adding a new downstream consumer is a new subscription — zero code changes.

### 2. Immutable Records with `with` Expressions

Every pipeline stage produces a *new* `DocumentMetadata` rather than mutating the old one. The `record` type gives `with` expressions for free:

```csharp
var enriched = document with
{
    Status = DocumentStatus.MetadataEnriched,
    Tags = result.Tags
};
```

This makes data flow explicit and prevents accidental side effects between stages.

### 3. Interface-First with Stub Implementations

All business logic lives behind interfaces (`IClassificationService`, `IOcrService`, `IMetadataEnrichmentService`). Stubs return deterministic data so the pipeline can be exercised without Azure AI resources. Swap to real implementations by changing DI registrations in `Program.cs` — no function code changes.

### 4. Isolated Worker Model

The Functions runtime runs in a separate process from the worker (`dotnet-isolated`). This is the modern model for .NET; the old in-process model is deprecated.

### 5. JWT Authentication Middleware (HTTP entry point)

The HTTP ingestion endpoint (`POST /api/documents`) is secured by custom `IFunctionsWorkerMiddleware`:

- **Production mode** (`Authentication:Bypass = false`): Validates Azure AD Bearer JWT tokens. Checks the OIDC discovery endpoint for signing keys. Requires the `DocumentContributor` role claim. Returns 401 for missing/expired/invalid tokens, 403 for valid tokens without the role.
- **Development mode** (`Authentication:Bypass = true`): Creates a synthetic `dev-user@local` identity with the `DocumentContributor` role. No token needed.
- Identity flows from the validated token's `upn`/`preferred_username` claim into `DocumentMetadata.UploadedBy`, then propagates through the entire pipeline.

### 6. Correlation ID for End-to-End Tracing

Every `DocumentEvent` carries a `CorrelationId` (GUID) that stays constant across the entire pipeline. Combined with Application Insights, you can query "show me everything that happened for document X" across all functions, services, and Logic Apps.

## The Document Lifecycle

```
Uploaded ──► Classified ──► ContentExtracted ──► MetadataEnriched ──► Approved
                                                                    │
                                                                    └──► Rejected
```

| State | Trigger | Function | Service Called | Output Event |
|---|---|---|---|---|
| Uploaded | HTTP POST | `IngestDocument` | `ServiceBusClient` | `DocumentUploaded` |
| Classified | `classify-document` sub | `OnDocumentUploaded` | `IClassificationService` | `DocumentClassified` |
| ContentExtracted | `extract-content` sub | `ClassifyDocument` | `IOcrService` | `DocumentContentExtracted` |
| MetadataEnriched | `enrich-metadata` sub | `ExtractDocumentContent` | `IMetadataEnrichmentService` | `DocumentMetadataEnriched` |
| Approved/Rejected | `ready-for-review` sub | Logic Apps | Human review | `DocumentApproved` / `DocumentRejected` |

## Service Bus Topology

**Topic:** `document-events`

| Subscription | Filter Rule | Consumer |
|---|---|---|
| (entry point) | HTTP `POST /api/documents` | `IngestDocument` function publishes `DocumentUploaded` |
| `classify-document` | `EventType = "DocumentUploaded"` | `OnDocumentUploaded` function |
| `extract-content` | `EventType = "DocumentClassified"` | `ClassifyDocument` function |
| `enrich-metadata` | `EventType = "DocumentContentExtracted"` | `ExtractDocumentContent` function |
| `ready-for-review` | `EventType = "DocumentMetadataEnriched"` | `EnrichDocumentMetadata` function + Logic Apps trigger |

Filter rules are defined on the subscription in Azure (not in code), keeping filtering at the broker level — functions never see events they don't need.

## Common Development Workflows

### Adding a New Pipeline Stage

1. Add the new state to `DocumentStatus.cs` enum
2. Create a new message record in `src/DocumentProcessing.Contracts/Messages/` inheriting `DocumentEvent`
3. Add a new service interface in `src/DocumentProcessing.Core/Services/`
4. Add a stub implementation
5. Register the stub in `Program.cs` DI
6. Create a new function in `src/DocumentProcessing.Functions/Functions/` with the appropriate Service Bus trigger and subscription
7. Add the subscription to the Service Bus topology (Azure portal or Bicep)

### Replacing a Stub with a Real Service

1. Add the NuGet package (e.g. `Azure.AI.FormRecognizer`)
2. Create the real implementation class implementing the existing interface
3. Update `Program.cs` DI registration — swap `StubXxxService` for the real implementation
4. Add connection string / key to `local.settings.json` (and Key Vault references for production)

No function code changes needed.

### Creating a New Event Type

1. Add a new class in `src/DocumentProcessing.Contracts/Messages/` inheriting `DocumentEvent`
2. Add stage-specific properties (confidence, page count, etc.)
3. If it represents a state transition, add the corresponding value to `DocumentStatus`

### Running Tests
```bash
dotnet test DocumentProcessing.sln
```

## Configuration

### local.settings.json
```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "ServiceBusConnection": "<your-connection-string>"
  }
}
```

- `FUNCTIONS_WORKER_RUNTIME` must be `dotnet-isolated` (not `dotnet`)
- `ServiceBusConnection` — local emulator or real namespace connection string
- `AzureWebJobsStorage` — `UseDevelopmentStorage=true` for Azurite, or a real storage account

## Error Handling Strategy

1. **Log on entry** — correlation ID, document ID, relevant context
2. **Call the service** — with a `CancellationToken` for graceful shutdown
3. **Log on success** — measurable results (pages extracted, fields enriched, confidence)
4. **Let exceptions bubble** — the Functions runtime handles retry. After max retries, the message goes to the dead-letter queue where Logic Apps monitors it.

No `try/catch` in function bodies (beyond what service implementations do internally). The runtime retry policy + dead-letter + Logic Apps monitoring forms a complete error-handling chain.

## Production Readiness: What to Swap

| Component | Current (Stub) | Production |
|---|---|---|
| Classification | `StubClassificationService` (file extension) | Azure AI Document Intelligence custom classifier |
| OCR | `StubOcrService` (placeholder text) | `Azure.AI.FormRecognizer.DocumentAnalysisClient` |
| Metadata enrichment | `StubMetadataEnrichmentService` (basic tags) | Azure AI Language entity recognition + custom NER |
| Service Bus connection | `local.settings.json` localhost | Azure Service Bus connection string from Key Vault |
| Storage | `UseDevelopmentStorage=true` (Azurite) | Real Azure Storage account |
| Secrets | Plain text in `local.settings.json` | Azure Key Vault references / Managed Identity |

## Best Practices

### Code Style
- Use C# `record` types for data transfer objects — immutable, value semantics, `with` expressions
- One interface per capability, one method per interface (ISP)
- Return result records from service methods, not tuples or out parameters
- File-scoped namespaces (`namespace Foo.Bar;`)

### Service Design
- Each service interface should be a single-method abstraction returning a strongly-typed result record
- Stubs should return deterministic, recognizable data (not `null` or `string.Empty`)
- Register services as singletons in the Functions DI container

### Performance
- Use `CancellationToken` throughout the async chain for graceful shutdown
- Avoid blocking calls — all service methods are `async`
- Batch operations where the Azure SDK supports it (not needed for stubs)

## Dependencies

### NuGet Packages (Functions Project)
- `Microsoft.Azure.Functions.Worker` (2.52.0) — isolated worker runtime
- `Microsoft.Azure.Functions.Worker.Sdk` (2.0.7) — build tooling
- `Microsoft.Azure.Functions.Worker.Extensions.ServiceBus` (5.24.0) — Service Bus bindings

### External (Azure, not in repo)
- Azure Service Bus namespace with `document-events` topic and subscriptions
- Azure Logic App for the approval workflow
- (Production) Azure AI Document Intelligence, Azure AI Language, Azure Key Vault

## Git Workflow

### Version Control Guidelines
- **NEVER** commit changes without user approval. Ask systematically for approval before committing.
- Commit messages should be clear and follow convention:
  - `ai-tooling:` AI agents, automation commands, workflows, or other AI-enabled developer tooling
  - `feat:` New feature
  - `fix:` Bug fix
  - `docs:` Documentation
  - `style:` Formatting
  - `refactor:` Code restructuring
  - `test:` Adding tests
  - `chore:` Maintenance tasks
- **NEVER** mention AI/Claude authorship in commit messages (no "Generated with Claude Code", "AI-assisted", etc.)

## Troubleshooting

### Build Errors
- Confirm .NET 10 SDK is installed: `dotnet --list-sdks`
- Confirm Azure Functions Core Tools v4: `func --version`
- Run `dotnet restore DocumentProcessing.sln` before `dotnet build`

### Runtime Errors
- **"Service Bus connection failed"** — verify `ServiceBusConnection` in `local.settings.json`, ensure the namespace exists and is accessible
- **"No functions found"** — confirm `<OutputType>Exe</OutputType>` is in the Functions `.csproj` and `FUNCTIONS_WORKER_RUNTIME` is `dotnet-isolated`
- **Messages not being received** — verify the topic and subscriptions exist in Azure Service Bus, and that filter rules match the `EventType` property

### Development Server Issues
- Azurite must be running if using `UseDevelopmentStorage=true`
- The Functions host (`func start`) requires the Azure Functions Core Tools to be installed globally

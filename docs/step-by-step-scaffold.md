# Step-by-Step: Scaffolding the Document Processing & Approval Pipeline (.NET 10 + Azure)

This guide recreates this exact project from an empty folder. Every command, every file, in order.

---

## Prerequisites

- **.NET 10 SDK** (10.0.300 or later) — [dotnet.microsoft.com](https://dotnet.microsoft.com)
- **Azure Functions Core Tools v4** — `npm install -g azure-functions-core-tools@4`
- A terminal (PowerShell, bash, or cmd) at the repo root

---

## Step 1: Create the Directory Structure

```powershell
mkdir -p src/DocumentProcessing.Contracts/Models
mkdir -p src/DocumentProcessing.Contracts/Messages
mkdir -p src/DocumentProcessing.Functions/Functions
mkdir -p src/DocumentProcessing.Core/Services
```

Result:

```
src/
├── DocumentProcessing.Contracts/
│   ├── Models/
│   └── Messages/
├── DocumentProcessing.Functions/
│   └── Functions/
└── DocumentProcessing.Core/
    └── Services/
```

---

## Step 2: Create the Solution File

`DocumentProcessing.sln` at the repo root:

```sln

Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "DocumentProcessing.Contracts", "src\DocumentProcessing.Contracts\DocumentProcessing.Contracts.csproj", "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "DocumentProcessing.Functions", "src\DocumentProcessing.Functions\DocumentProcessing.Functions.csproj", "{B2C3D4E5-F6A7-8901-BCDE-F12345678901}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "DocumentProcessing.Core", "src\DocumentProcessing.Core\DocumentProcessing.Core.csproj", "{C3D4E5F6-A7B8-9012-CDEF-123456789012}"
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Release|Any CPU = Release|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.Release|Any CPU.Build.0 = Release|Any CPU
		{B2C3D4E5-F6A7-8901-BCDE-F12345678901}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{B2C3D4E5-F6A7-8901-BCDE-F12345678901}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{B2C3D4E5-F6A7-8901-BCDE-F12345678901}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{B2C3D4E5-F6A7-8901-BCDE-F12345678901}.Release|Any CPU.Build.0 = Release|Any CPU
		{C3D4E5F6-A7B8-9012-CDEF-123456789012}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{C3D4E5F6-A7B8-9012-CDEF-123456789012}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{C3D4E5F6-A7B8-9012-CDEF-123456789012}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{C3D4E5F6-A7B8-9012-CDEF-123456789012}.Release|Any CPU.Build.0 = Release|Any CPU
	EndGlobalSection
EndGlobal
```

> **Why manual?** The `dotnet new sln` + `dotnet sln add` workflow works too, but writing the `.sln` by hand gives you full control over the GUIDs and avoids tooling drift.

---

## Step 3: Create the Contracts Project

**File:** `src/DocumentProcessing.Contracts/DocumentProcessing.Contracts.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>DocumentProcessing.Contracts</RootNamespace>
  </PropertyGroup>

</Project>
```

This is a **plain class library** — no Azure packages needed. It holds pure C# types that every other project references.

### 3a. The Status Enum

**File:** `src/DocumentProcessing.Contracts/Models/DocumentStatus.cs`

```csharp
namespace DocumentProcessing.Contracts.Models;

public enum DocumentStatus
{
    Uploaded = 1,
    Classified = 2,
    ContentExtracted = 3,
    MetadataEnriched = 4,
    Approved = 5,
    Rejected = 6
}
```

Every document moves through these states in order. `Approved` and `Rejected` are terminal.

### 3b. The Document Metadata Record

**File:** `src/DocumentProcessing.Contracts/Models/DocumentMetadata.cs`

```csharp
namespace DocumentProcessing.Contracts.Models;

public sealed record DocumentMetadata
{
    public string DocumentId { get; init; } = Guid.NewGuid().ToString("D");
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public string BlobUri { get; init; } = string.Empty;
    public string UploadedBy { get; init; } = string.Empty;
    public DateTimeOffset UploadedAt { get; init; }
    public DocumentStatus Status { get; init; } = DocumentStatus.Uploaded;
    public string? Classification { get; init; }
    public string? ExtractedText { get; init; }
    public Dictionary<string, string> Tags { get; init; } = new();
    public string? ReviewedBy { get; init; }
    public string? ReviewNotes { get; init; }
}
```

> **Why a `record`?** The `with` expression (`document with { Status = ... }`) is used throughout the pipeline to produce immutable, modified copies at each stage. Records give you that for free.

### 3c. The Base Event Record

**File:** `src/DocumentProcessing.Contracts/Models/DocumentEvent.cs`

```csharp
namespace DocumentProcessing.Contracts.Models;

public abstract record DocumentEvent
{
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("D");
    public DocumentMetadata Document { get; init; } = null!;
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public string EventType => GetType().Name;
}
```

Every message flowing through Service Bus wraps a `DocumentMetadata` plus a correlation ID for tracing.

### 3d. The Six Message Types

Each lifecycle transition gets its own strongly-typed message:

| File | Event | Extra fields |
|---|---|---|
| `Messages/DocumentUploaded.cs` | Entry point | (none beyond base) |
| `Messages/DocumentClassified.cs` | After classification | `Confidence` (double) |
| `Messages/DocumentContentExtracted.cs` | After OCR | `PageCount`, `ProcessingDuration` |
| `Messages/DocumentMetadataEnriched.cs` | After enrichment | `EnrichedFieldCount` |
| `Messages/DocumentApproved.cs` | Terminal — approved | `ArchivePath` |
| `Messages/DocumentRejected.cs` | Terminal — rejected | `RejectionReason` |

Example — `DocumentClassified.cs`:

```csharp
using DocumentProcessing.Contracts.Models;

namespace DocumentProcessing.Contracts.Messages;

public sealed record DocumentClassified : DocumentEvent
{
    public double Confidence { get; init; }
}
```

All six follow the same pattern: inherit `DocumentEvent`, add stage-specific fields.

---

## Step 4: Create the Core Project

**File:** `src/DocumentProcessing.Core/DocumentProcessing.Core.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>DocumentProcessing.Core</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\DocumentProcessing.Contracts\DocumentProcessing.Contracts.csproj" />
  </ItemGroup>

</Project>
```

References the Contracts project so it can use the models.

### 4a. Service Interfaces (the "what")

Create three interfaces under `Services/`:

**`IClassificationService.cs`** — classifies document type, returns a label + confidence score:

```csharp
using DocumentProcessing.Contracts.Models;

namespace DocumentProcessing.Core.Services;

public interface IClassificationService
{
    Task<ClassificationResult> ClassifyAsync(DocumentMetadata document, CancellationToken ct = default);
}

public sealed record ClassificationResult
{
    public string Label { get; init; } = "Unknown";
    public double Confidence { get; init; }
}
```

**`IOcrService.cs`** — extracts text from a blob URI:

```csharp
using DocumentProcessing.Contracts.Models;

namespace DocumentProcessing.Core.Services;

public interface IOcrService
{
    Task<OcrResult> ExtractAsync(string blobUri, CancellationToken ct = default);
}

public sealed record OcrResult
{
    public string ExtractedText { get; init; } = string.Empty;
    public int PageCount { get; init; }
    public TimeSpan ProcessingDuration { get; init; }
}
```

**`IMetadataEnrichmentService.cs`** — enriches metadata with tags and entities:

```csharp
using DocumentProcessing.Contracts.Models;

namespace DocumentProcessing.Core.Services;

public interface IMetadataEnrichmentService
{
    Task<EnrichmentResult> EnrichAsync(DocumentMetadata document, CancellationToken ct = default);
}

public sealed record EnrichmentResult
{
    public Dictionary<string, string> Tags { get; init; } = new();
    public int EnrichedFieldCount => Tags.Count;
}
```

### 4b. Stub Implementations (the "how" — placeholder)

Three stub classes that return canned data:

- **`StubClassificationService.cs`** — classifies by file extension (`.pdf` → "PDF Document", etc.)
- **`StubOcrService.cs`** — returns `"[stub OCR text]"` with 1 page
- **`StubMetadataEnrichmentService.cs`** — returns basic tags (document_type, file_size_kb, etc.)

Each is registered in the Functions DI container in Step 5.

---

## Step 5: Create the Functions Project

**File:** `src/DocumentProcessing.Functions/DocumentProcessing.Functions.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AzureFunctionsVersion>v4</AzureFunctionsVersion>
    <OutputType>Exe</OutputType>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>DocumentProcessing.Functions</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Azure.Functions.Worker" Version="2.52.0" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="2.0.7" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.ServiceBus" Version="5.24.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\DocumentProcessing.Contracts\DocumentProcessing.Contracts.csproj" />
    <ProjectReference Include="..\DocumentProcessing.Core\DocumentProcessing.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="host.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
    <None Update="local.settings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>Never</CopyToPublishDirectory>
    </None>
  </ItemGroup>

</Project>
```

Key points:
- `<OutputType>Exe</OutputType>` — required for isolated worker model
- `<AzureFunctionsVersion>v4</AzureFunctionsVersion>` — latest Functions runtime
- Three NuGet packages: the worker SDK, the worker runtime, and the Service Bus extension

### 5a. Host Configuration

**`host.json`:**

```json
{
  "version": "2.0",
  "logging": {
    "applicationInsights": {
      "samplingSettings": {
        "isEnabled": true,
        "excludedTypes": "Request"
      },
      "enableLiveMetricsFilters": true
    },
    "logLevel": {
      "default": "Information",
      "Host.Results": "Information",
      "Function": "Information"
    }
  }
}
```

**`local.settings.json`:**

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "ServiceBusConnection": "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=localdevkey"
  }
}
```

> `FUNCTIONS_WORKER_RUNTIME` must be `dotnet-isolated` (not `dotnet`). The old in-process model is deprecated for .NET 10.

### 5b. Program.cs — Dependency Injection

```csharp
using DocumentProcessing.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddSingleton<IClassificationService, StubClassificationService>();
        services.AddSingleton<IOcrService, StubOcrService>();
        services.AddSingleton<IMetadataEnrichmentService, StubMetadataEnrichmentService>();
    })
    .Build();

await host.RunAsync();
```

`ConfigureFunctionsWorkerDefaults()` sets up the isolated worker and registers the `AuthenticationMiddleware`. The `ServiceBusClient` and three stubs are registered as singletons — swap them for real implementations in production.

### 5c. The Five Function Triggers

Four functions subscribe to Service Bus subscriptions; one is an HTTP trigger. They form a chain:

```
IngestDocument [HTTP] → OnDocumentUploaded → ClassifyDocument → ExtractDocumentContent → EnrichDocumentMetadata
```

**File: `Functions/IngestDocument.cs`** (HTTP entry point)

- **Trigger:** HTTP `POST /api/documents`
- **Action:** Validates the request body, reads the authenticated identity from the JWT middleware, creates a `DocumentMetadata`, publishes `DocumentUploaded` to `document-events` via `ServiceBusClient`
- **Output:** Returns `201 Created` with document ID, status, and correlation ID

**File: `Functions/OnDocumentUploaded.cs`**

- **Trigger:** Service Bus subscription `classify-document` on topic `document-events`
- **Input:** `DocumentUploaded` message
- **Action:** Calls `IClassificationService.ClassifyAsync()`
- **Output:** Returns `DocumentClassified` (which the Functions runtime publishes back to the topic)

**File: `Functions/ClassifyDocument.cs`**

- **Trigger:** Service Bus subscription `extract-content` on topic `document-events`
- **Input:** `DocumentClassified` message
- **Action:** Calls `IOcrService.ExtractAsync()`
- **Output:** Returns `DocumentContentExtracted`

**File: `Functions/ExtractDocumentContent.cs`**

- **Trigger:** Service Bus subscription `enrich-metadata` on topic `document-events`
- **Input:** `DocumentContentExtracted` message
- **Action:** Calls `IMetadataEnrichmentService.EnrichAsync()`
- **Output:** Returns `DocumentMetadataEnriched`

**File: `Functions/EnrichDocumentMetadata.cs`**

- **Trigger:** Service Bus subscription `ready-for-review` on topic `document-events`
- **Input:** `DocumentMetadataEnriched` message
- **Action:** Logs the enrichment summary — this is the **hand-off point** to Logic Apps
- **Output:** None (terminal in the Functions pipeline)

Each function follows the same pattern:

```csharp
[Function(nameof(FunctionName))]
public async Task<OutputMessageType?> Run(
    [ServiceBusTrigger(
        topicName: "document-events",
        subscriptionName: "subscription-name",
        Connection = "ServiceBusConnection")]
    InputMessageType input,
    CancellationToken ct)
{
    // ... process, log, return next event
}
```

The return type determines what gets published back to the topic. Return `null` to suppress output.

---

## Step 6: Restore and Build

```powershell
dotnet restore DocumentProcessing.sln
dotnet build DocumentProcessing.sln --no-restore
```

Expected output:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Step 7: Run Locally (requires Service Bus emulator or real namespace)

```powershell
cd src/DocumentProcessing.Functions
func start
```

> Without a reachable Service Bus, the Functions host starts but the triggers won't receive messages. Update `ServiceBusConnection` in `local.settings.json` to point to a real Azure Service Bus namespace or a local emulator.

---

## File Inventory (25 source files + 17 test files)

```
DocumentProcessing.sln
src/
├── DocumentProcessing.Contracts/
│   ├── DocumentProcessing.Contracts.csproj
│   ├── Models/
│   │   ├── DocumentStatus.cs
│   │   ├── DocumentMetadata.cs
│   │   └── DocumentEvent.cs
│   └── Messages/
│       ├── DocumentUploaded.cs
│       ├── DocumentClassified.cs
│       ├── DocumentContentExtracted.cs
│       ├── DocumentMetadataEnriched.cs
│       ├── DocumentApproved.cs
│       └── DocumentRejected.cs
├── DocumentProcessing.Core/
│   ├── DocumentProcessing.Core.csproj
│   └── Services/
│       ├── IClassificationService.cs
│       ├── IOcrService.cs
│       ├── IMetadataEnrichmentService.cs
│       ├── StubClassificationService.cs
│       ├── StubOcrService.cs
│       └── StubMetadataEnrichmentService.cs
├── DocumentProcessing.Functions/
│   ├── DocumentProcessing.Functions.csproj
│   ├── Program.cs
│   ├── host.json
│   ├── local.settings.json
│   ├── Configuration/
│   │   └── AuthOptions.cs
│   ├── Models/
│   │   └── IngestDocumentRequest.cs
│   ├── Middleware/
│   │   └── AuthenticationMiddleware.cs
│   └── Functions/
│       ├── IngestDocument.cs
│       ├── OnDocumentUploaded.cs
│       ├── ClassifyDocument.cs
│       ├── ExtractDocumentContent.cs
│       └── EnrichDocumentMetadata.cs
└── DocumentProcessing.Tests/
    ├── DocumentProcessing.Tests.csproj
    ├── Helpers/
    │   ├── MockHelpers.cs
    │   └── TestDataFactory.cs
    ├── Contracts/
    │   ├── DocumentStatusTests.cs
    │   ├── DocumentMetadataTests.cs
    │   └── DocumentEventTests.cs
    ├── Core/
    │   ├── StubClassificationServiceTests.cs
    │   ├── StubOcrServiceTests.cs
    │   └── StubMetadataEnrichmentServiceTests.cs
    ├── Functions/
    │   ├── IngestDocumentTests.cs
    │   ├── OnDocumentUploadedTests.cs
    │   ├── ClassifyDocumentTests.cs
    │   ├── ExtractDocumentContentTests.cs
    │   └── EnrichDocumentMetadataTests.cs
    ├── Middleware/
    │   └── AuthenticationMiddlewareTests.cs
    └── Integration/
        └── PipelineIntegrationTests.cs
```

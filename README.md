# Document Processing & Approval Pipeline

A **serverless document processing backend** built on .NET 10 and Azure. Documents flow through an automated pipeline — classification → OCR → metadata enrichment — then enter a human approval workflow orchestrated by Logic Apps. Every stage communicates through Azure Service Bus, making the system fully decoupled, event-driven, and resilient to failures.

## Architecture

```
                       ┌───────────────────────────────────────────────────────────┐
                       │                 Azure Service Bus                          │
                       │              Topic: document-events                         │
                       │                                                             │
                       │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐        │
                       │  │ classify-   │  │ extract-    │  │ enrich-     │  ready- │
                       │  │ document    │  │ content     │  │ metadata    │  for-   │
                       │  │             │  │             │  │             │  review │
                       │  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘  ┌──┬──┐ │
                       └─────────┼────────────────┼────────────────┼─────────┼──┼──┘
                                 │                │                │         │
                        ┌────────▼────────┐ ┌─────▼──────┐ ┌──────▼──────┐ ┌▼──────▼──┐
                        │ OnDocument      │ │ Classify   │ │ Extract     │ │ Enrich   │
                        │ Uploaded        │ │ Document   │ │ Document    │ │ Document │
                        │                 │ │            │ │ Content     │ │ Metadata │
                        │ Classifies doc  │ │ Runs OCR   │ │ Enriches    │ │ Hands    │
                        │ type            │ │ extraction │ │ metadata    │ │ off to   │
                        └─────────────────┘ └────────────┘ └─────────────┘ │ Logic    │
                                                                           │ Apps     │
                                                                           └──────────┘
                                                                                │
                                                                ┌───────────────▼───────────────┐
                                                                │       Azure Logic Apps        │
                                                                │                                │
                                                                │  ┌──────────┐  ┌───────────┐  │
                                                                │  │ Approval │  │ Compliance│  │
                                                                │  │ Workflow │  │ Archiving │  │
                                                                │  └──────────┘  └───────────┘  │
                                                                └────────────────────────────────┘
```

## Document Lifecycle

```
Uploaded ──► Classified ──► ContentExtracted ──► MetadataEnriched ──► Approved
                                                                    │
                                                                    └──► Rejected
```

1. **Uploaded** — Document lands in blob storage; entry event published
2. **Classified** — Document type determined (Invoice, Contract, Report, etc.)
3. **ContentExtracted** — OCR extracts text from the document
4. **MetadataEnriched** — Entities, key-values, and structured data extracted
5. **Approved / Rejected** — Human reviews via Logic Apps; terminal states

## Technology Stack

| Technology | Role |
|---|---|
| **.NET 10 + C# 14** | Application code (Contracts, Core, Functions) |
| **Azure Service Bus** | Reliable pub/sub messaging via topics and subscriptions |
| **Azure Functions v4** | Serverless compute — classification, OCR, enrichment (isolated worker model) |
| **Azure Logic Apps** | Long-running human approval workflows with Office 365/Teams connectors |
| **Azure AI Document Intelligence** | Production OCR and classification (stubs in dev) |
| **Azure AI Language** | Production entity extraction and NER (stubs in dev) |

## Prerequisites

- **.NET 10 SDK** (10.0.300 or later) — [download](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Azure Functions Core Tools v4** — `npm install -g azure-functions-core-tools@4`
- (Optional) **Azurite** for local blob storage emulation

## Quick Start

```powershell
# Restore and build
dotnet restore DocumentProcessing.sln
dotnet build DocumentProcessing.sln --no-restore

# Run the Functions host locally
cd src/DocumentProcessing.Functions
func start
```

> **Note:** Without a reachable Azure Service Bus, the Functions host starts but triggers won't receive messages. Update `ServiceBusConnection` in `local.settings.json` to point to your Service Bus namespace.

## Project Structure

```
src/
├── DocumentProcessing.Contracts/       # Shared types — referenced by all projects
│   ├── Models/
│   │   ├── DocumentStatus.cs           # Lifecycle enum (6 states)
│   │   ├── DocumentMetadata.cs         # Immutable document record
│   │   └── DocumentEvent.cs            # Base event with CorrelationId
│   └── Messages/
│       ├── DocumentUploaded.cs         # Entry-point event
│       ├── DocumentClassified.cs       # + Confidence
│       ├── DocumentContentExtracted.cs # + PageCount, ProcessingDuration
│       ├── DocumentMetadataEnriched.cs # + EnrichedFieldCount
│       ├── DocumentApproved.cs         # Terminal — + ArchivePath
│       └── DocumentRejected.cs         # Terminal — + RejectionReason
├── DocumentProcessing.Core/           # Business logic (interfaces + stubs)
│   └── Services/
│       ├── IClassificationService.cs
│       ├── IOcrService.cs
│       ├── IMetadataEnrichmentService.cs
│       ├── StubClassificationService.cs
│       ├── StubOcrService.cs
│       └── StubMetadataEnrichmentService.cs
└── DocumentProcessing.Functions/      # Azure Functions host
    ├── Program.cs                     # DI setup
    ├── host.json                      # Runtime config
    ├── local.settings.json            # Local connection strings
    └── Functions/
        ├── OnDocumentUploaded.cs      # classify-document subscription
        ├── ClassifyDocument.cs        # extract-content subscription
        ├── ExtractDocumentContent.cs  # enrich-metadata subscription
        └── EnrichDocumentMetadata.cs  # ready-for-review subscription
```

## Key Design Decisions

- **Topic, not queues** — pub/sub enables adding new subscribers (audit, metrics, dashboards) without changing existing code
- **Immutable records** — `with` expressions produce new `DocumentMetadata` at each stage; no mutation, no side effects
- **Interfaces + stubs** — pipeline runs end-to-end without Azure AI resources; swap to real services via DI only
- **Correlation ID** — a single GUID traces every step across Functions, Service Bus, and Logic Apps in Application Insights

## Production Roadmap

See [docs/project-deep-dive.md](docs/project-deep-dive.md#production-readiness-what-to-swap) for the full swap list. In short:

| Current (stub) | Production |
|---|---|
| File-extension classification | Azure AI Document Intelligence custom classifier |
| Placeholder OCR text | `Azure.AI.FormRecognizer.DocumentAnalysisClient` |
| Basic tag extraction | Azure AI Language entity recognition + custom NER |
| `local.settings.json` secrets | Azure Key Vault + Managed Identity |
| Not yet deployed | ARM/Bicep template for Logic Apps workflow |

## Further Reading

- [Project Deep Dive](docs/project-deep-dive.md) — architecture, lifecycle, design rationale, and error handling
- [Step-by-Step Scaffold](docs/step-by-step-scaffold.md) — recreate this project from an empty folder
- [Technology Analysis](analysis.txt) — why Service Bus, Functions, and Logic Apps were chosen for this project

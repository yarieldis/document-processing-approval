# Document Processing & Approval Pipeline — Deep Dive

## What Is This Project?

A **serverless document processing backend** built on .NET 10 and Azure. Documents enter the system through a secured HTTP endpoint (`POST /api/documents`), flow through an automated pipeline (classification → OCR → metadata enrichment), and then enter a human approval workflow. Every stage communicates through Azure Service Bus, making the system fully decoupled, event-driven, and resilient to failures.

This is **Alternative 2** from the original analysis — chosen because it highlights all three Azure technologies in their natural roles:

| Technology | Role in this system |
|---|---|
| **Azure Service Bus** | Reliable message bus carrying document lifecycle events between decoupled stages |
| **Azure Functions** | Custom compute — classification, OCR extraction, metadata enrichment |
| **Azure Logic Apps** | Orchestration — multi-step approval workflows, email/Teams notifications, compliance archiving |

---

## Architecture Overview

```
                            POST /api/documents (JWT auth)
                                      │
                          ┌───────────▼───────────┐
                          │    IngestDocument      │
                          │    HTTP Trigger        │
                          │    Publishes           │
                          │    DocumentUploaded    │
                          └───────────┬───────────┘
                                      │
                         ┌────────────▼──────────────────────────────────────────────┐
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

### Key Architectural Decisions

1. **Topic, not queues.** A single topic (`document-events`) with multiple subscriptions lets each function receive only the events it cares about. If we add a new subscriber later (e.g. an audit logger), it's a new subscription — zero code changes to existing functions.

2. **Isolated worker model.** The Functions runtime runs in a separate process from the worker. This is the modern model for .NET; the old in-process model is deprecated.

3. **Immutable records with `with` expressions.** Every pipeline stage produces a *new* `DocumentMetadata` with updated fields rather than mutating state. This makes the data flow explicit and prevents accidental side effects between stages.

4. **Stub-first design.** All business logic sits behind interfaces (`IClassificationService`, `IOcrService`, `IMetadataEnrichmentService`). The stubs let you run and test the pipeline end-to-end before integrating with real Azure AI services.

5. **JWT authentication middleware.** The HTTP ingestion endpoint is secured by custom `IFunctionsWorkerMiddleware`. In production, it validates Azure AD Bearer tokens and enforces the `DocumentContributor` role. In development, a bypass mode creates a synthetic identity — no real token needed. The authenticated user's identity (`upn` claim) flows into `DocumentMetadata.UploadedBy` and propagates through the entire pipeline.

6. **Comprehensive test suite.** 97 tests across all layers: contracts (record defaults, equality, serialization), core stubs (every code path), Service Bus functions (mocked dependencies), HTTP function logic (identity extraction), middleware (bypass principal, options), and end-to-end pipeline integration using real stubs.

---

## The Document Lifecycle

A document passes through exactly six states:

```
Uploaded ──► Classified ──► ContentExtracted ──► MetadataEnriched ──► Approved
                                                                   │
                                                                   └──► Rejected
```

### State 1: Uploaded
A client sends a `POST` request to `/api/documents` with the document's metadata (file name, MIME type, size, blob URI). The `IngestDocument` HTTP-triggered function validates the request, extracts the authenticated user's identity from the JWT (populating `UploadedBy`), and publishes a `DocumentUploaded` event to the Service Bus topic. The `DocumentMetadata` captures the file name, MIME type, size, blob URI, uploader identity, and timestamp.

### State 2: Classified
The `OnDocumentUploaded` function picks up the event and calls the classification service. The service determines the document type — "Invoice", "Contract", "Report", "Image", etc. The classification label and confidence score are stamped onto the metadata, and a `DocumentClassified` event is published.

### State 3: ContentExtracted
The `ClassifyDocument` function takes the now-classified document and runs OCR via the OCR service. The extracted text is stored in `ExtractedText`, along with the page count and how long the operation took. A `DocumentContentExtracted` event is published.

### State 4: MetadataEnriched
The `ExtractDocumentContent` function feeds the extracted text (and any existing metadata) to the enrichment service. This service extracts entities, key-value pairs, dates, monetary amounts, and other structured data. The result is a set of key/value tags (e.g. `invoice_total=1420.00`, `vendor=Acme Corp`, `due_date=2026-07-01`). A `DocumentMetadataEnriched` event is published.

### States 5 & 6: Approved / Rejected (Terminal)
The `EnrichDocumentMetadata` function logs the final enriched state and acts as the hand-off to Logic Apps. At this point, the automated pipeline is done. A Logic App (triggered by the `ready-for-review` subscription) takes over:

- Sends an approval email to the relevant manager
- Presents the extracted metadata for review
- On **approval**: publishes `DocumentApproved`, archives the document to compliance storage, logs an audit record
- On **rejection**: publishes `DocumentRejected` with a reason, notifies the uploader

---

## Component Details

### Contracts Project (`DocumentProcessing.Contracts`)

The **shared language** of the system. Every project references it, and every message flowing through Service Bus is a type defined here.

**Why a separate contracts project?**
- Prevents circular dependencies
- Can be published as a NuGet package for external consumers (e.g. the Logic App connector layer)
- Versioned independently — if the schema evolves, the contracts version tells you what changed

**The `DocumentEvent` base record:**

Every event carries:
- `CorrelationId` — a GUID that stays constant across the entire pipeline, enabling end-to-end tracing
- `Document` — the full `DocumentMetadata` snapshot at this stage
- `OccurredAt` — UTC timestamp for ordering and latency measurement
- `EventType` — computed from the C# type name, useful for routing and filtering

### Core Project (`DocumentProcessing.Core`)

The **business logic layer**. Defines what the system does without coupling to how Azure Functions invokes it.

**Interface design pattern:**
Each capability is a single-method interface returning a result record:

```csharp
public interface IOcrService
{
    Task<OcrResult> ExtractAsync(string blobUri, CancellationToken ct = default);
}
```

This makes them easy to mock in tests, easy to swap in production (change the DI registration, not the function code), and easy to decorate (add logging, retry, or caching via the decorator pattern).

**The stubs** return deterministic data so the pipeline can be exercised without any Azure AI resources:

| Stub | Returns | Production replacement |
|---|---|---|
| `StubClassificationService` | Label based on file extension | Azure AI Document Intelligence custom classifier |
| `StubOcrService` | `"[stub OCR text]"`, 1 page | `Azure.AI.FormRecognizer.DocumentAnalysisClient` |
| `StubMetadataEnrichmentService` | 4 basic tags | Azure AI Language (NER, key-phrase extraction, custom models) |

### Functions Project (`DocumentProcessing.Functions`)

The **compute host**. Five functions: one HTTP-triggered entry point plus four Service Bus-triggered functions forming the automated pipeline.

#### Function: `IngestDocument`
- **Trigger:** HTTP `POST /api/documents`
- **Input:** `IngestDocumentRequest` (fileName, contentType, fileSizeBytes, blobUri)
- **Output:** Publishes `DocumentUploaded` to Service Bus, returns `201 Created`

This is the **entry point** into the system. It is the only public endpoint. The `AuthenticationMiddleware` ensures only authorized callers (with a valid Azure AD Bearer token bearing the `DocumentContributor` role) can submit documents. The function extracts the caller's identity from the validated token, stamps it into `DocumentMetadata.UploadedBy`, serializes a `DocumentUploaded` event, and publishes it to the `document-events` Service Bus topic.

In local development (`Authentication:Bypass = true`), the middleware creates a synthetic `dev-user@local` identity — no real token needed.

#### Function: `OnDocumentUploaded`
- **Subscription:** `classify-document`
- **Filter:** `Subject = "Uploaded"` (configured on the subscription in Azure, not in code)
- **Input:** `DocumentUploaded`
- **Output:** `DocumentClassified`

This is the first automated step. It reads the raw upload event and determines what kind of document it's dealing with.

#### Function: `ClassifyDocument`
- **Subscription:** `extract-content`
- **Input:** `DocumentClassified`
- **Output:** `DocumentContentExtracted`

Runs OCR. In production, this would call Azure AI Document Intelligence's prebuilt `prebuilt-read` or `prebuilt-layout` model. The stub just returns placeholder text.

#### Function: `ExtractDocumentContent`
- **Subscription:** `enrich-metadata`
- **Input:** `DocumentContentExtracted`
- **Output:** `DocumentMetadataEnriched`

Extracts structured data from the raw OCR text. This is where domain-specific value emerges — an invoice becomes a set of key/values (`total`, `tax`, `line_items`), a contract becomes parties, dates, and clauses.

#### Function: `EnrichDocumentMetadata`
- **Subscription:** `ready-for-review`
- **Input:** `DocumentMetadataEnriched`
- **Output:** None

The final function in the chain. It logs the enrichment summary and **returns `void`** — this is intentional. The `ready-for-review` subscription is also monitored by Logic Apps, which picks up the same event and begins the human workflow.

#### Why the return type matters

When a function returns a non-null value, the Functions runtime serializes it and sends it to the **output binding** (in this case, the `document-events` topic). When it returns `void` or `null`, nothing is published. The `EnrichDocumentMetadata` function deliberately returns `void` because the automated pipeline ends here.

#### Dependency Injection

`Program.cs` registers middleware and services with `AddSingleton()`:

- **`AuthenticationMiddleware`** — registered via `ConfigureFunctionsWorkerDefaults(context => context.UseMiddleware<AuthenticationMiddleware>())`. Runs before every function invocation.
- **`AuthOptions`** — bound from the `Authentication` config section (bypass mode, tenant/client IDs).
- **`ServiceBusClient`** — singleton used by `IngestDocument` to programmatically publish messages.
- **Three stub services** — `IClassificationService`, `IOcrService`, `IMetadataEnrichmentService`.

In production, you would:

1. Add the Azure SDK clients (`DocumentAnalysisClient`, `TextAnalyticsClient`)
2. Register them via `AddSingleton()` with real endpoint and key configuration
3. Replace the stub registrations with the real implementations
4. Set `Authentication:Bypass = false` and configure `TenantId`/`ClientId` for real Azure AD

No function code changes — just DI registration.

---

## The Service Bus Topology

### Topic: `document-events`

A single topic carries all document lifecycle events. This is a **pub/sub** model — multiple subscribers can receive the same event independently.

### Subscriptions (4, plus HTTP entry point + Logic Apps)

| Subscription | Filter rule | Purpose |
|---|---|---|
| `classify-document` | `EventType = "DocumentUploaded"` | Triggers classification |
| `extract-content` | `EventType = "DocumentClassified"` | Triggers OCR |
| `enrich-metadata` | `EventType = "DocumentContentExtracted"` | Triggers enrichment |
| `ready-for-review` | `EventType = "DocumentMetadataEnriched"` | Triggers Logic Apps + final log |

The filter rules ensure each subscriber only receives the events it cares about. The Functions runtime can also apply these filters at the binding level, but defining them on the subscription itself keeps filtering at the broker — your functions never see events they don't need.

### Why not queues?

Queues are **competing consumer** — one message, one receiver. Topics with subscriptions are **pub/sub** — one message, many receivers. If we later want to add:

- A real-time dashboard that listens to all events
- An audit log that archives every state transition
- A metrics collector that tracks pipeline latency

Each is a new subscription. With queues, you'd have to fan-out manually. With topics, you add a subscription and it just works.

### Dead-letter handling

Messages that fail processing (e.g. corrupt document, OCR timeout) are dead-lettered after the configured retry count. Logic Apps monitors the dead-letter queue and can:

- Send an alert to a Teams channel
- Create a ticket in the support system
- Notify the document uploader with the failure reason

---

## Authentication & Authorization

### The `AuthenticationMiddleware`

Custom `IFunctionsWorkerMiddleware` that runs before every function invocation. The middleware:

1. **Skips non-HTTP functions** — Service Bus triggers pass through untouched.
2. **Bypass mode** (dev): Creates a synthetic `ClaimsPrincipal` with `upn: dev-user@local`, `roles: DocumentContributor`. Stored in `FunctionContext.Items["AuthPrincipal"]`.
3. **Production mode**: Extracts the Bearer token from the `Authorization` header, validates it against Azure AD (OIDC discovery), checks the `roles` claim for `DocumentContributor`, and stores the `ClaimsPrincipal` in `FunctionContext.Items["AuthPrincipal"]`.

**Error responses:**
| Condition | HTTP status |
|---|---|
| Missing/invalid Authorization header | 401 |
| Malformed token (`CanReadToken` fails) | 400 |
| Expired token | 401 |
| Valid token but missing `DocumentContributor` role | 403 |
| Valid token + correct role | Principal stored, function executes |

### Identity Propagation

The `IngestDocument` function reads the principal from `FunctionContext.Items["AuthPrincipal"]` and calls `ExtractIdentity(principal)`:

```
UPN claim → preferred_username claim → name claim → sub claim → "unknown"
```

This value becomes `DocumentMetadata.UploadedBy` — a validated, non-spoofable identity that travels with the document through every stage of the pipeline.

### Production Setup (Azure)

1. Create an **App Registration** in Azure AD for the Function App
2. Define an **App Role** `DocumentContributor` (type: Users/Groups)
3. Assign the role to authorized users/groups in Enterprise Applications
4. Set app settings: `Authentication__Bypass = false`, `Authentication__TenantId = <tenant>`, `Authentication__ClientId = <app-reg-client-id>`

---

## How Logic Apps Fits In

Logic Apps doesn't have code in this repo — it's configured in the Azure portal or via ARM/Bicep. Here's what it does:

### Trigger
A Service Bus trigger on the `ready-for-review` subscription picks up `DocumentMetadataEnriched` events.

### The Approval Workflow

1. **Parse the event** — extract document metadata, tags, and blob URI
2. **Send approval email** — Outlook/Office 365 connector sends an email with:
   - Document name, classification, extracted tags
   - A link to view the document (blob URI with SAS token)
   - "Approve" and "Reject" buttons (via adaptive cards or a custom approval connector)
3. **Wait for response** — the workflow pauses until a reviewer acts
4. **On approval:**
   - Publish `DocumentApproved` to the `document-events` topic
   - Move the blob to the compliance archive container
   - Log an audit record to a SharePoint list or Azure Table Storage
5. **On rejection:**
   - Publish `DocumentRejected` to the `document-events` topic
   - Notify the uploader with the rejection reason
   - Optionally delete or quarantine the blob

### Why Logic Apps and not another Function?

- **Connectors** — Outlook, Teams, SharePoint, Approvals are built-in. A Function would need OAuth flows, Graph API calls, and polling logic.
- **Long-running workflows** — the approval might take days. Logic Apps handles the wait/resume natively via its stateful engine. A Function would timeout or need a complex durable function pattern.
- **Visibility** — Logic Apps provides a visual run history showing exactly where each approval is, without building a monitoring dashboard.

---

## Production Readiness: What to Swap

| Component | Current (stub) | Production |
|---|---|---|
| Classification | `StubClassificationService` (file extension) | Azure AI Document Intelligence custom classifier or a fine-tuned model |
| OCR | `StubOcrService` (placeholder text) | `Azure.AI.FormRecognizer` prebuilt-layout or prebuilt-read model |
| Metadata enrichment | `StubMetadataEnrichmentService` (basic tags) | Azure AI Language entity recognition + custom NER models |
| Service Bus connection | `local.settings.json` localhost string | Azure Service Bus connection string from Key Vault |
| Storage | `UseDevelopmentStorage=true` (Azurite) | Real Azure Storage account connection string |
| Secrets | Plain text in `local.settings.json` | Azure Key Vault references in `host.json` or Managed Identity |
| Logic Apps | Not yet deployed | ARM/Bicep template deploying the approval workflow |
| Authentication | `Authentication:Bypass = true` (synthetic identity) | `Authentication:Bypass = false` + Azure AD App Registration with `DocumentContributor` role |

---

## Error Handling Strategy

Each function follows the same pattern:

1. **Log on entry** — with correlation ID, document ID, and relevant context
2. **Call the service** — with a `CancellationToken` for graceful shutdown
3. **Log on success** — with measurable results (pages extracted, fields enriched, confidence score)
4. **Let exceptions bubble** — the Functions runtime handles retry. After max retries, the message goes to the dead-letter queue where Logic Apps picks it up

No `try/catch` in the function bodies (beyond what the service implementations do internally). The runtime retry policy + dead-letter + Logic Apps monitoring forms a complete error-handling chain.

---

## Tracing and Observability

Every event carries a `CorrelationId` that's constant across the entire pipeline. Combined with:

- **Application Insights** (configured in `host.json`) — distributed tracing, dependency tracking
- **Structured logging** — every log message includes the correlation ID and document ID
- **Service Bus message properties** — the correlation ID is also set as a message property for broker-level tracing

This means you can query "show me everything that happened for document X" across all functions, services, and Logic Apps in a single Application Insights query.

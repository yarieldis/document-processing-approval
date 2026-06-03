# Logic App Workflow Plan — Document Approval Pipeline

This document provides a detailed, step-by-step plan for creating the Azure Logic App(s) that complete the document processing pipeline. The Logic App picks up where the automated pipeline ends (`EnrichDocumentMetadata` hands off at the `ready-for-review` subscription) and orchestrates the human approval workflow, compliance archiving, and terminal event publishing.

---

## 1. Architecture Overview

```
                                              Azure Service Bus
                                         Topic: document-events
                                         Subscription: ready-for-review
                                         Filter: EventType = "DocumentMetadataEnriched"
                                                      │
                                      ┌───────────────▼───────────────┐
                                      │     Logic App: Document       │
                                      │     Approval Workflow         │
                                      │                               │
                                      │  ┌─────────────────────────┐  │
                                      │  │ 1. Parse Service Bus    │  │
                                      │  │    message              │  │
                                      │  │ 2. Send approval email  │  │
                                      │  │ 3. Wait for response    │  │
                                      │  │ 4. Branch on decision   │  │
                                      │  └──────┬──────────┬───────┘  │
                                      │         │          │          │
                                      └─────────┼──────────┼──────────┘
                                                │          │
                                           APPROVED    REJECTED
                                                │          │
                          ┌─────────────────────▼──┐  ┌────▼──────────────────────┐
                          │  Approval Sub-Workflow  │  │  Rejection Sub-Workflow    │
                          │                         │  │                            │
                          │  • Archive blob         │  │  • Publish DocumentRejected│
                          │  • Publish Document-    │  │  • Notify uploader         │
                          │    Approved              │  │  • Optionally quarantine   │
                          │  • Log to audit store   │  │    blob                    │
                          └─────────────────────────┘  └────────────────────────────┘
```

---

## 2. Prerequisites

Before creating the Logic App, ensure the following are in place:

| Prerequisite | Purpose |
|---|---|
| Azure Service Bus namespace with `document-events` topic | Message source and destination |
| `ready-for-review` subscription on the topic | Triggers the Logic App when `DocumentMetadataEnriched` fires |
| Office 365 / Outlook connector authorization | Sends approval emails |
| Azure Blob Storage account (same as pipeline) | Archive / quarantine blobs |
| (Optional) SharePoint list or Table Storage | Audit log for approval decisions |
| Azure AD App Registration (for the Functions HTTP endpoint) | Already created as part of the Functions auth setup |

---

## 3. Logic App Structure — Two Approaches

### Option A: Single Logic App with Conditional Branches (Recommended)

One Logic App triggered by Service Bus. Uses a **condition** action to branch on the approval response. Simpler to manage, fewer resources.

**Pros:** Single resource, shared error handling, easier to monitor.
**Cons:** The approval action blocks until response; concurrent runs scale independently.

### Option B: Two Logic Apps (Approval + Terminal Actions)

One Logic App handles the approval (email → wait → response). A second Logic App is triggered by a follow-up event or HTTP call to perform the terminal actions (archive + publish). More decoupled but adds complexity.

**Pros:** Separation of concerns, independent scaling.
**Cons:** Two resources, orchestration glue needed.

**This plan uses Option A** — a single Logic App with conditional branches.

---

## 4. Step-by-Step: Building the Logic App in the Azure Portal

### Step 1: Create the Logic App Resource

1. Go to **Azure Portal → Create a resource → Logic App**
2. **Subscription / Resource Group**: same as your Functions app
3. **Logic App name**: `doc-approval-workflow`
4. **Plan type**: **Consumption** (pay-per-execution) — or **Standard** if you need VNet integration
5. **Region**: same as your Service Bus namespace (to minimize latency)
6. Click **Review + create**

> **Consumption vs Standard**: Consumption is simpler and sufficient for a document approval workflow (infrequent executions, stateless). Standard is for high-throughput or VNet scenarios.

---

### Step 2: Add the Service Bus Trigger

1. Open the Logic App Designer (blank workflow)
2. Search for **"Service Bus"** in the connectors
3. Select **When a message is received in a topic subscription (peek-lock)**
4. Configure:
   - **Connection name**: `servicebus-doc-events`
   - **Authentication type**: Connection string (or Managed Identity if configured)
   - **Connection string**: your Service Bus namespace connection string
   - **Topic name**: `document-events`
   - **Subscription name**: `ready-for-review`
   - **Peek lock**: Enabled (ensures at-least-once delivery — the Logic App completes the message after processing)

> **Why peek-lock?** If the Logic App fails midway, the message returns to the subscription for retry. Use **Receive and delete** only if you're OK with losing the message on failure.

---

### Step 3: Parse the Service Bus Message (JSON)

The Service Bus trigger delivers the message body as a Base64-encoded string by default.

1. Add an action: **Data Operations → Parse JSON**
2. **Content**: `@base64ToString(triggerBody()?['ContentData'])`
3. **Schema**: Use the `DocumentMetadataEnriched` JSON shape (see below)

<details>
<summary>JSON Schema for Parse JSON action</summary>

```json
{
  "type": "object",
  "properties": {
    "correlationId": { "type": "string" },
    "document": {
      "type": "object",
      "properties": {
        "documentId": { "type": "string" },
        "fileName": { "type": "string" },
        "contentType": { "type": "string" },
        "fileSizeBytes": { "type": "integer" },
        "blobUri": { "type": "string" },
        "uploadedBy": { "type": "string" },
        "uploadedAt": { "type": "string" },
        "status": { "type": "string" },
        "classification": { "type": "string" },
        "extractedText": { "type": "string" },
        "tags": {
          "type": "object",
          "properties": {
            "document_type": { "type": "string" },
            "file_size_kb": { "type": "string" },
            "uploaded_date": { "type": "string" },
            "pipeline_status": { "type": "string" }
          }
        },
        "reviewedBy": { "type": ["string", "null"] },
        "reviewNotes": { "type": ["string", "null"] }
      }
    },
    "enrichedFieldCount": { "type": "integer" },
    "eventType": { "type": "string" },
    "occurredAt": { "type": "string" }
  }
}
```
</details>

---

### Step 4: Send Approval Email (Outlook / Office 365)

1. Add an action: **Office 365 Outlook → Send approval email**
2. Configure:
   - **To**: The approver's email (can be hardcoded for demo, or looked up via a SharePoint list / Azure AD group for production)
   - **Subject**: `Document Approval Required: @{body('Parse_JSON')?['document']?['fileName']}`
   - **User Options**: `Approve`, `Reject` (comma-separated for the adaptive card buttons)
   - **Details** (adaptive card body):

```
Document ID: @{body('Parse_JSON')?['document']?['documentId']}
Uploaded by: @{body('Parse_JSON')?['document']?['uploadedBy']}
Classification: @{body('Parse_JSON')?['document']?['classification']}
File size: @{body('Parse_JSON')?['document']?['fileSizeBytes']} bytes

Extracted tags:
@{join(items(body('Parse_JSON')?['document']?['tags'])?['x-ms-tokenised-on-value'], '
')}
```

> **Note**: The `tags` field is a JSON object. Use a **Select** action before the email to transform `tags` into key=value lines if you want cleaner formatting.

---

### Step 5: Build the Tags Table (Optional but recommended)

1. Add an action **before** the email: **Data Operations → Select**
2. **From**: `body('Parse_JSON')?['document']?['tags']`
3. **Map**: `item()?['key']` → key, `item()?['value']` → value
4. In the email body, reference the Select output as a formatted list.

---

### Step 6: Condition — Approve or Reject

The **Send approval email** action is a **blocking** action — the Logic App run pauses until a reviewer clicks Approve or Reject (or the timeout expires).

1. Add a **Condition** action after the approval email
2. **Condition**: `@equals(body('Send_approval_email')?['selectedOption'], 'Approve')`

#### Branch A: Approved

```
If selectedOption == "Approve"
│
├── Action: Compose (archive path)
│   Value: /archive/@{body('Parse_JSON')?['document']?['classification']}/@{body('Parse_JSON')?['document']?['documentId']}/@{body('Parse_JSON')?['document']?['fileName']}
│
├── Action: Azure Blob Storage → Copy blob
│   Source blob: @{body('Parse_JSON')?['document']?['blobUri']}
│   Destination container: archive
│   Destination blob: @{outputs('Compose_archive_path')}
│
├── Action: Service Bus → Send message
│   Topic: document-events
│   Message content: DocumentApproved JSON with archivePath
│   Session ID: @{body('Parse_JSON')?['correlationId']}
│
├── Action: SharePoint / Table Storage → Create item (audit log)
│   (Optional) Logs: documentId, decision, reviewer, timestamp
│
└── Action: Complete the Service Bus message
    (Lock token: @{trigger()?['LockToken']})
```

#### Branch B: Rejected

```
If selectedOption == "Reject"
│
├── Action: Compose (rejection reason)
│   Value: @{coalesce(body('Send_approval_email')?['comments'], 'No reason provided')}
│
├── Action: Service Bus → Send message
│   Topic: document-events
│   Message content: DocumentRejected JSON with rejectionReason
│   Session ID: @{body('Parse_JSON')?['correlationId']}
│
├── Action: Office 365 Outlook → Send email
│   To: @{body('Parse_JSON')?['document']?['uploadedBy']}
│   Subject: Document Rejected: @{body('Parse_JSON')?['document']?['fileName']}
│   Body: Your document was rejected.
│          Reason: @{outputs('Compose_rejection_reason')}
│
├── Action (Optional): Azure Blob Storage → Delete blob or move to quarantine
│
└── Action: Complete the Service Bus message
    (Lock token: @{trigger()?['LockToken']})
```

---

### Step 7: Publish Terminal Events to Service Bus

Both branches publish a terminal event back to `document-events`. Construct the JSON bodies:

**DocumentApproved message:**
```json
{
  "correlationId": "@{body('Parse_JSON')?['correlationId']}",
  "document": {
    ... (same document from parsed message),
    "status": "Approved",
    "reviewedBy": "@{body('Send_approval_email')?['responder']}",
    "reviewNotes": "Approved via Logic Apps"
  },
  "archivePath": "@{outputs('Compose_archive_path')}",
  "eventType": "DocumentApproved",
  "occurredAt": "@{utcNow()}"
}
```

**DocumentRejected message:**
```json
{
  "correlationId": "@{body('Parse_JSON')?['correlationId']}",
  "document": {
    ... (same document from parsed message),
    "status": "Rejected",
    "reviewedBy": "@{body('Send_approval_email')?['responder']}",
    "reviewNotes": "@{outputs('Compose_rejection_reason')}"
  },
  "rejectionReason": "@{outputs('Compose_rejection_reason')}",
  "eventType": "DocumentRejected",
  "occurredAt": "@{utcNow()}"
}
```

> **Note on `document` field**: The full `DocumentMetadata` from the enriched event is carried forward. In Logic Apps, you can either construct the full JSON manually or store the original parsed document in a variable and add/override fields.

---

### Step 8: Configure Timeout and Error Handling

1. **Approval timeout**: In the **Send approval email** action, under **Settings → Timeout**, set a duration (e.g., `P7D` for 7 days). After timeout, the Logic App run is marked as **TimedOut**.

2. **Timeout handling**: Add a **parallel branch** after the timeout using a **Configure run after** setting on the condition:
   - **has timed out** → Send a reminder email, notify an escalation contact, or auto-reject.

3. **Dead-letter monitoring**: Add a separate Logic App (or a second trigger in this one) that monitors the Service Bus **dead-letter queue**:
   - Trigger: **Service Bus → When a message is received in a queue (peek-lock)** on the dead-letter path
   - Action: **Office 365 → Send email** or **Teams → Post message** to notify ops

4. **Retry policy**: For Service Bus **Send message** actions, set the retry policy to **Exponential** with 3-5 retries.

---

## 5. Local Development & Testing

### 5.1 Testing the Logic App Locally

Logic Apps cannot run fully locally, but you can test the integration points:

1. **Send a test `DocumentMetadataEnriched` message to Service Bus** using the Azure Portal's **Service Bus Explorer** or via the `az` CLI:
   ```bash
   az servicebus topic subscription send \
     --namespace-name <namespace> \
     --topic-name document-events \
     --subscription-name ready-for-review \
     --body @test-enriched-event.json
   ```

2. **Test the Functions pipeline first**: Run `func start` and POST a document via `curl` to `/api/documents`. Verify the pipeline completes and the `DocumentMetadataEnriched` event appears on the `ready-for-review` subscription.

3. **Trigger the Logic App**: Once the message is on the subscription, the Logic App picks it up and the approval email is sent.

### 5.2 End-to-End Integration Test Flow

```
1. Start Functions host:            func start
2. POST a document:                 curl -X POST http://localhost:7071/api/documents
                                      -H "Content-Type: application/json"
                                      -d '{"fileName":"invoice.pdf","contentType":"application/pdf",...}'
3. Pipeline runs through all 4 functions
4. DocumentMetadataEnriched appears on ready-for-review subscription
5. Logic App triggers → sends approval email
6. Reviewer clicks Approve/Reject
7. Logic App publishes DocumentApproved/DocumentRejected to document-events
8. Verify terminal event in Service Bus Explorer
```

> For local dev without real Service Bus, simulate steps 4-7 by publishing `DocumentMetadataEnriched` directly to Azure Service Bus using the Portal or CLI.

---

## 6. Producing the `DocumentUploaded` Event from Blob Storage (Optional)

Currently, documents enter via the HTTP endpoint (`POST /api/documents`). If you also want to trigger the pipeline from a blob upload:

### Option A: Add a Blob-Triggered Function

```csharp
[Function("OnBlobUploaded")]
public async Task Run(
    [BlobTrigger("incoming/{name}", Connection = "AzureWebJobsStorage")] BlobClient blob,
    string name,
    FunctionContext context,
    CancellationToken ct)
{
    // 1. Read blob metadata
    // 2. Create DocumentMetadata
    // 3. Publish DocumentUploaded to Service Bus
}
```

**File location:** `src/DocumentProcessing.Functions/Functions/OnBlobUploaded.cs`

**Note:** A blob trigger does not carry user identity — `UploadedBy` would be set to a service account or `"blob-upload"`. Use the HTTP endpoint when identity matters.

### Option B: Use a Logic App with Blob Trigger

1. Create a second Logic App with **Azure Blob Storage → When a blob is added or modified** trigger
2. Container: `incoming`
3. Action: **Service Bus → Send message** to `document-events` with a `DocumentUploaded` event

This avoids code changes but adds a Logic App dependency for the ingestion path.

---

## 7. Production Deployment

### 7.1 Export as ARM/Bicep Template

Once the Logic App is working in the Azure Portal, export it for Infrastructure-as-Code:

1. Go to the Logic App resource → **Export template**
2. Download the ARM template
3. Convert to Bicep (optional): `az bicep decompile --file template.json`
4. Store in `infra/logic-app.bicep` in the repo

### 7.2 Configuration Values to Parametrize

| Parameter | Source |
|---|---|
| `serviceBusConnectionString` | Key Vault `@Microsoft.KeyVault(SecretUri=...)` |
| `approverEmail` | App setting or Key Vault |
| `storageAccountConnectionString` | Key Vault |
| `sharePointSiteUrl` (optional) | App setting |

### 7.3 Managed Identity

For production, configure the Logic App with a **System-assigned Managed Identity** and grant it:
- **Azure Service Bus Data Sender** on the `document-events` topic
- **Azure Service Bus Data Receiver** on the `ready-for-review` subscription
- **Storage Blob Data Contributor** on the archive/quarantine containers
- (Optional) **Sites.ReadWrite.All** for SharePoint integration

---

## 8. File Summary

If implementing the Bicep template and any supporting code:

```
infra/
└── logic-app.bicep                 # Logic App ARM/Bicep template

src/DocumentProcessing.Functions/Functions/
└── OnBlobUploaded.cs               # (Optional) Blob-triggered ingestion function
```

---

## 9. Quick Reference: Actions in the Logic App

| Step | Action | Connector |
|---|---|---|
| 1 | When a message is received (peek-lock) | Service Bus |
| 2 | Parse JSON | Data Operations |
| 3 | Select (format tags) | Data Operations |
| 4 | Send approval email | Office 365 Outlook |
| 5 | Condition (Approve / Reject) | Control |
| 6a | Copy blob to archive | Azure Blob Storage |
| 6b | Send DocumentApproved message | Service Bus |
| 6c | Log to audit store | SharePoint / Table Storage |
| 7a | Send DocumentRejected message | Service Bus |
| 7b | Send rejection notification | Office 365 Outlook |
| 7c | Quarantine blob (optional) | Azure Blob Storage |
| 8 | Complete the message (both branches) | Service Bus |

# Deploy the Logic App workflow.
# Prerequisites: Azure CLI logged in, resource group exists,
# API connections created (Service Bus, Office 365, Blob Storage).
#
# Usage:
#   ./infra/deploy-logic-app.ps1 -ResourceGroup docprocessing-rg `
#       -ServiceBusNamespace sb-docprocessing `
#       -ApproverEmail manager@contoso.com

param(
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [Parameter(Mandatory)] [string] $ServiceBusNamespace,
    [Parameter(Mandatory)] [string] $ApproverEmail,
    [Parameter()] [string] $Location = 'eastus',
    [Parameter()] [string] $AppName = 'doc-approval-workflow',
    [Parameter()] [string] $StorageAccountName = 'docprocessingstorage',
    [Parameter()] [string] $ServiceBusConnectionName = 'servicebus',
    [Parameter()] [string] $Office365ConnectionName = 'office365',
    [Parameter()] [string] $BlobStorageConnectionName = 'azureblob'
)

$ErrorActionPreference = 'Stop'

Write-Host "Deploying Logic App workflow..." -ForegroundColor Cyan
Write-Host "  Resource Group:   $ResourceGroup"
Write-Host "  Location:         $Location"
Write-Host "  App Name:         $AppName"
Write-Host "  Approver:         $ApproverEmail"
Write-Host "  Service Bus:      $ServiceBusNamespace"
Write-Host "  Storage Account:  $StorageAccountName"

# ── Step 0: Ensure resource group exists ──
$rgExists = az group exists --name $ResourceGroup
if ($rgExists -eq 'false') {
    Write-Host "Creating resource group: $ResourceGroup"
    az group create --name $ResourceGroup --location $Location
}

# ── Step 1: Validate the Bicep template ──
Write-Host "`nValidating Bicep template..."
az deployment group validate `
    --resource-group $ResourceGroup `
    --template-file $PSScriptRoot/logic-app.bicep `
    --parameters appName=$AppName `
                 serviceBusNamespace=$ServiceBusNamespace `
                 approverEmail=$ApproverEmail `
                 storageAccountName=$StorageAccountName `
                 serviceBusConnectionName=$ServiceBusConnectionName `
                 office365ConnectionName=$Office365ConnectionName `
                 blobStorageConnectionName=$BlobStorageConnectionName

# ── Step 2: What-if deployment ──
Write-Host "`nRunning what-if..."
az deployment group what-if `
    --resource-group $ResourceGroup `
    --template-file $PSScriptRoot/logic-app.bicep `
    --parameters appName=$AppName `
                 serviceBusNamespace=$ServiceBusNamespace `
                 approverEmail=$ApproverEmail `
                 storageAccountName=$StorageAccountName `
                 serviceBusConnectionName=$ServiceBusConnectionName `
                 office365ConnectionName=$Office365ConnectionName `
                 blobStorageConnectionName=$BlobStorageConnectionName

# ── Step 3: Confirm and deploy ──
$confirm = Read-Host "`nProceed with deployment? (y/n)"
if ($confirm -ne 'y') {
    Write-Host "Deployment cancelled."
    exit 0
}

Write-Host "`nDeploying..."
az deployment group create `
    --resource-group $ResourceGroup `
    --template-file $PSScriptRoot/logic-app.bicep `
    --parameters appName=$AppName `
                 serviceBusNamespace=$ServiceBusNamespace `
                 approverEmail=$ApproverEmail `
                 storageAccountName=$StorageAccountName `
                 serviceBusConnectionName=$ServiceBusConnectionName `
                 office365ConnectionName=$Office365ConnectionName `
                 blobStorageConnectionName=$BlobStorageConnectionName

Write-Host "`nDeployment complete!" -ForegroundColor Green
Write-Host "Logic App URL: https://portal.azure.com/#@/resource/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$ResourceGroup/providers/Microsoft.Logic/workflows/$AppName"

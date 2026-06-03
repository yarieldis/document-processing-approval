@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Base name used to derive resource names')
param appName string = 'doc-approval-workflow'

@description('Name of the Service Bus namespace')
param serviceBusNamespace string

@description('Service Bus topic name')
param serviceBusTopic string = 'document-events'

@description('Service Bus subscription name for the approval trigger')
param serviceBusSubscription string = 'ready-for-review'

@description('Name of the existing Service Bus API connection in the resource group')
param serviceBusConnectionName string = 'servicebus'

@description('Name of the existing Office 365 API connection in the resource group')
param office365ConnectionName string = 'office365'

@description('Name of the existing Azure Blob Storage API connection')
param blobStorageConnectionName string = 'azureblob'

@description('Email address of the approver')
param approverEmail string

@description('Storage account name for archive/quarantine blobs')
param storageAccountName string

@description('Archive container name')
param archiveContainer string = 'archive'

@description('Quarantine container name (for rejected documents)')
param quarantineContainer string = 'quarantine'

@description('Approval timeout in ISO 8601 duration format (default 7 days)')
param approvalTimeout string = 'P7D'

@description('Whether to enable the Logic App on creation')
param enabled bool = true

// ═══════════════════════════════════════════════════════════════════
// Logic App Resource
// ═══════════════════════════════════════════════════════════════════

resource logicApp 'Microsoft.Logic/workflows@2019-05-01' = {
  name: appName
  location: location
  properties: {
    state: enabled ? 'Enabled' : 'Disabled'
    definition: {
      '$schema': 'https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#'
      contentVersion: '1.0.0.0'
      parameters: {
        '$connections': {
          defaultValue: {}
          type: 'Object'
        }
      }
      triggers: {
        // ── Trigger: Service Bus message on ready-for-review subscription ──
        'When_a_message_is_received_in_a_topic_subscription': {
          type: 'ApiConnection'
          inputs: {
            host: {
              connection: {
                name: '@parameters(\'$connections\')[\'servicebus\'][\'connectionId\']'
              }
            }
            method: 'get'
            path: '@{encodeURIComponent(encodeURIComponent(\'${serviceBusTopic}\'))}/subscriptions/@{encodeURIComponent(\'${serviceBusSubscription}\')}/messages/head/peek'
            queries: {
              subscriptionType: 'Main'
            }
          }
          recurrence: {
            frequency: 'Minute'
            interval: 1
          }
          splitOn: '@triggerBody()'
        }
      }
      actions: {
        // ── Parse the Service Bus message body ──
        'Parse_Enriched_Event': {
          type: 'ParseJson'
          inputs: {
            content: '@base64ToString(triggerBody()?[\'ContentData\'])'
            schema: {
              type: 'object'
              properties: {
                correlationId: { type: 'string' }
                eventType: { type: 'string' }
                occurredAt: { type: 'string' }
                enrichedFieldCount: { type: 'integer' }
                document: {
                  type: 'object'
                  properties: {
                    documentId: { type: 'string' }
                    fileName: { type: 'string' }
                    contentType: { type: 'string' }
                    fileSizeBytes: { type: 'integer' }
                    blobUri: { type: 'string' }
                    uploadedBy: { type: 'string' }
                    uploadedAt: { type: 'string' }
                    status: { type: 'string' }
                    classification: { type: ['string', 'null'] }
                    extractedText: { type: ['string', 'null'] }
                    tags: { type: 'object' }
                    reviewedBy: { type: ['string', 'null'] }
                    reviewNotes: { type: ['string', 'null'] }
                  }
                }
              }
            }
          }
          runAfter: {}
        }

        // ── Format tags into a readable string for the email ──
        'Format_Tags': {
          type: 'Select'
          inputs: {
            from: '@body(\'Parse_Enriched_Event\')?[\'document\']?[\'tags\']'
            select: '@{item()?[\'key\']}: @{item()?[\'value\']}'
          }
          runAfter: {
            'Parse_Enriched_Event': ['Succeeded']
          }
        }

        // ── Send approval email ──
        'Send_Approval_Email': {
          type: 'ApiConnection'
          inputs: {
            host: {
              connection: {
                name: '@parameters(\'$connections\')[\'office365\'][\'connectionId\']'
              }
            }
            method: 'post'
            path: '/v2/Mail'
            body: {
              Message: {
                To: [
                  {
                    EmailAddress: {
                      Address: approverEmail
                    }
                  }
                ]
                Subject: "Document Approval Required: @{body('Parse_Enriched_Event')?['document']?['fileName']}"
                Importance: 'Normal'
                Body: {
                  ContentType: 'HTML'
                  Content: "<h2>Document Ready for Review</h2>
<p><b>Document ID:</b> @{body('Parse_Enriched_Event')?['document']?['documentId']}</p>
<p><b>Uploaded by:</b> @{body('Parse_Enriched_Event')?['document']?['uploadedBy']}</p>
<p><b>Classification:</b> @{body('Parse_Enriched_Event')?['document']?['classification']}</p>
<p><b>File size:</b> @{body('Parse_Enriched_Event')?['document']?['fileSizeBytes']} bytes</p>
<p><b>Correlation ID:</b> @{body('Parse_Enriched_Event')?['correlationId']}</p>
<br/>
<h3>Extracted Tags</h3>
<pre>@{join(body('Format_Tags'), decodeUriComponent('%0D%0A'))}</pre>
<br/>
<p><a href='@{body('Parse_Enriched_Event')?['document']?['blobUri']}'>View Document</a></p>
<p><i>Reply to this email with <b>APPROVE</b> or <b>REJECT</b> followed by any comments.</i></p>"
                }
                IsReadReceiptRequested: false
              }
              SaveToSentItems: true
            }
            retryPolicy: {
              type: 'None'
            }
          }
          runAfter: {
            'Format_Tags': ['Succeeded']
          }
        }

        // ── Initialize approval response variables ──
        'Initialize_Decision': {
          type: 'InitializeVariable'
          inputs: {
            variables: [
              {
                name: 'decision'
                type: 'string'
                value: ''
              }
              {
                name: 'reviewerComments'
                type: 'string'
                value: ''
              }
            ]
          }
          runAfter: {
            'Send_Approval_Email': ['Succeeded']
          }
        }

        // ── Wait for approval response (polling) ──
        'Wait_For_Approval': {
          type: 'Until'
          expression: '@not(equals(variables(\'decision\'), \'\'))'
          limit: {
            timeout: approvalTimeout
            count: 5000
          }
          actions: {
            'Check_Response_Email': {
              type: 'ApiConnection'
              inputs: {
                host: {
                  connection: {
                    name: '@parameters(\'$connections\')[\'office365\'][\'connectionId\']'
                  }
                }
                method: 'get'
                path: '/v2/Mail/Messages'
                queries: {
                  '$filter': "contains(Subject, '@{body('Parse_Enriched_Event')?['document']?['documentId']}') and IsRead eq false"
                  '$top': 10
                }
              }
            }
            'Parse_Response': {
              type: 'Foreach'
              foreach: '@body(\'Check_Response_Email\')?[\'value\']'
              actions: {
                'Check_Body': {
                  type: 'If'
                  expression: '@contains(toUpper(triggerBody()?[\'text\']?[\'body\']?[\'content\']), \'APPROVE\')'
                  actions: {
                    'Set_Approved': {
                      type: 'SetVariable'
                      inputs: {
                        name: 'decision'
                        value: 'Approved'
                      }
                    }
                    'Set_Comments': {
                      type: 'SetVariable'
                      inputs: {
                        name: 'reviewerComments'
                        value: "@{items('Parse_Response')?['text']?['body']?['content']}"
                      }
                    }
                  }
                  else: {
                    'Check_Reject': {
                      type: 'If'
                      expression: '@contains(toUpper(triggerBody()?[\'text\']?[\'body\']?[\'content\']), \'REJECT\')'
                      actions: {
                        'Set_Rejected': {
                          type: 'SetVariable'
                          inputs: {
                            name: 'decision'
                            value: 'Rejected'
                          }
                        }
                        'Set_Reject_Comments': {
                          type: 'SetVariable'
                          inputs: {
                            name: 'reviewerComments'
                            value: "@{items('Parse_Response')?['text']?['body']?['content']}"
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
            'Delay': {
              type: 'Wait'
              inputs: {
                interval: {
                  count: 5
                  unit: 'Minute'
                }
              }
            }
          }
          runAfter: {
            'Initialize_Decision': ['Succeeded']
          }
        }

        // ── Condition: Approve or Reject ──
        'Evaluate_Decision': {
          type: 'If'
          expression: '@equals(variables(\'decision\'), \'Approved\')'
          actions: {
            // ══ APPROVED BRANCH ══

            'Compose_Archive_Path': {
              type: 'Compose'
              inputs: "/archive/@{body('Parse_Enriched_Event')?['document']?['classification']}/@{body('Parse_Enriched_Event')?['document']?['documentId']}/@{body('Parse_Enriched_Event')?['document']?['fileName']}"
              runAfter: {}
            }

            'Copy_Blob_To_Archive': {
              type: 'ApiConnection'
              inputs: {
                host: {
                  connection: {
                    name: '@parameters(\'$connections\')[\'azureblob\'][\'connectionId\']'
                  }
                }
                method: 'post'
                path: '/v2/datasets/@{encodeURIComponent(storageAccountName)}/files'
                queries: {
                  sourcePath: "@{body('Parse_Enriched_Event')?['document']?['blobUri']}"
                  destinationPath: "@{outputs('Compose_Archive_Path')}"
                  overwrite: true
                  queryParametersSingleEncoded: true
                }
              }
              runAfter: {
                'Compose_Archive_Path': ['Succeeded']
              }
            }

            'Compose_Approved_Event': {
              type: 'Compose'
              inputs: {
                correlationId: "@{body('Parse_Enriched_Event')?['correlationId']}"
                eventType: 'DocumentApproved'
                occurredAt: '@{utcNow()}'
                document: {
                  documentId: "@{body('Parse_Enriched_Event')?['document']?['documentId']}"
                  fileName: "@{body('Parse_Enriched_Event')?['document']?['fileName']}"
                  contentType: "@{body('Parse_Enriched_Event')?['document']?['contentType']}"
                  fileSizeBytes: "@{body('Parse_Enriched_Event')?['document']?['fileSizeBytes']}"
                  blobUri: "@{body('Parse_Enriched_Event')?['document']?['blobUri']}"
                  uploadedBy: "@{body('Parse_Enriched_Event')?['document']?['uploadedBy']}"
                  uploadedAt: "@{body('Parse_Enriched_Event')?['document']?['uploadedAt']}"
                  status: 'Approved'
                  classification: "@{body('Parse_Enriched_Event')?['document']?['classification']}"
                  tags: "@{body('Parse_Enriched_Event')?['document']?['tags']}"
                  reviewedBy: 'Logic Apps Approval Workflow'
                  reviewNotes: "@{variables('reviewerComments')}"
                }
                archivePath: "@{outputs('Compose_Archive_Path')}"
              }
              runAfter: {
                'Copy_Blob_To_Archive': ['Succeeded']
              }
            }

            'Publish_Approved_Event': {
              type: 'ApiConnection'
              inputs: {
                host: {
                  connection: {
                    name: '@parameters(\'$connections\')[\'servicebus\'][\'connectionId\']'
                  }
                }
                method: 'post'
                path: '/@{encodeURIComponent(encodeURIComponent(\'${serviceBusTopic}\'))}/messages'
                body: '@{outputs(\'Compose_Approved_Event\')}'
                queries: {
                  sessionId: "@{body('Parse_Enriched_Event')?['correlationId']}"
                }
              }
              runAfter: {
                'Compose_Approved_Event': ['Succeeded']
              }
            }

            'Complete_Approved_Message': {
              type: 'ApiConnection'
              inputs: {
                host: {
                  connection: {
                    name: '@parameters(\'$connections\')[\'servicebus\'][\'connectionId\']'
                  }
                }
                method: 'delete'
                path: '/@{encodeURIComponent(encodeURIComponent(\'${serviceBusTopic}\'))}/subscriptions/@{encodeURIComponent(\'${serviceBusSubscription}\')}/messages/@{encodeURIComponent(triggerOutputs()?[\'headers\']?[\'MessageId\'])}/lock/@{encodeURIComponent(triggerOutputs()?[\'headers\']?[\'LockToken\'])}'
              }
              runAfter: {
                'Publish_Approved_Event': ['Succeeded']
              }
            }
          }
          else: {
            // ══ REJECTED BRANCH ══

            'Compose_Rejection_Reason': {
              type: 'Compose'
              inputs: "@{if(empty(variables('reviewerComments')), 'No reason provided by reviewer', variables('reviewerComments'))}"
              runAfter: {}
            }

            'Compose_Rejected_Event': {
              type: 'Compose'
              inputs: {
                correlationId: "@{body('Parse_Enriched_Event')?['correlationId']}"
                eventType: 'DocumentRejected'
                occurredAt: '@{utcNow()}'
                document: {
                  documentId: "@{body('Parse_Enriched_Event')?['document']?['documentId']}"
                  fileName: "@{body('Parse_Enriched_Event')?['document']?['fileName']}"
                  contentType: "@{body('Parse_Enriched_Event')?['document']?['contentType']}"
                  fileSizeBytes: "@{body('Parse_Enriched_Event')?['document']?['fileSizeBytes']}"
                  blobUri: "@{body('Parse_Enriched_Event')?['document']?['blobUri']}"
                  uploadedBy: "@{body('Parse_Enriched_Event')?['document']?['uploadedBy']}"
                  uploadedAt: "@{body('Parse_Enriched_Event')?['document']?['uploadedAt']}"
                  status: 'Rejected'
                  classification: "@{body('Parse_Enriched_Event')?['document']?['classification']}"
                  tags: "@{body('Parse_Enriched_Event')?['document']?['tags']}"
                  reviewedBy: 'Logic Apps Approval Workflow'
                  reviewNotes: "@{variables('reviewerComments')}"
                }
                rejectionReason: "@{outputs('Compose_Rejection_Reason')}"
              }
              runAfter: {
                'Compose_Rejection_Reason': ['Succeeded']
              }
            }

            'Publish_Rejected_Event': {
              type: 'ApiConnection'
              inputs: {
                host: {
                  connection: {
                    name: '@parameters(\'$connections\')[\'servicebus\'][\'connectionId\']'
                  }
                }
                method: 'post'
                path: '/@{encodeURIComponent(encodeURIComponent(\'${serviceBusTopic}\'))}/messages'
                body: '@{outputs(\'Compose_Rejected_Event\')}'
                queries: {
                  sessionId: "@{body('Parse_Enriched_Event')?['correlationId']}"
                }
              }
              runAfter: {
                'Compose_Rejected_Event': ['Succeeded']
              }
            }

            'Notify_Uploader': {
              type: 'ApiConnection'
              inputs: {
                host: {
                  connection: {
                    name: '@parameters(\'$connections\')[\'office365\'][\'connectionId\']'
                  }
                }
                method: 'post'
                path: '/v2/Mail'
                body: {
                  Message: {
                    To: [
                      {
                        EmailAddress: {
                          Address: "@{body('Parse_Enriched_Event')?['document']?['uploadedBy']}"
                        }
                      }
                    ]
                    Subject: "Document Rejected: @{body('Parse_Enriched_Event')?['document']?['fileName']}"
                    Body: {
                      ContentType: 'HTML'
                      Content: "<p>Your document <b>@{body('Parse_Enriched_Event')?['document']?['fileName']}</b> has been rejected.</p><p><b>Reason:</b> @{outputs('Compose_Rejection_Reason')}</p><p>Document ID: @{body('Parse_Enriched_Event')?['document']?['documentId']}</p>"
                    }
                  }
                  SaveToSentItems: true
                }
              }
              runAfter: {
                'Publish_Rejected_Event': ['Succeeded']
              }
            }

            'Move_Blob_To_Quarantine': {
              type: 'ApiConnection'
              inputs: {
                host: {
                  connection: {
                    name: '@parameters(\'$connections\')[\'azureblob\'][\'connectionId\']'
                  }
                }
                method: 'post'
                path: '/v2/datasets/@{encodeURIComponent(storageAccountName)}/files'
                queries: {
                  sourcePath: "@{body('Parse_Enriched_Event')?['document']?['blobUri']}"
                  destinationPath: "/${quarantineContainer}/@{body('Parse_Enriched_Event')?['document']?['documentId']}/@{body('Parse_Enriched_Event')?['document']?['fileName']}"
                  overwrite: true
                  queryParametersSingleEncoded: true
                }
              }
              runAfter: {
                'Notify_Uploader': ['Succeeded']
              }
            }

            'Complete_Rejected_Message': {
              type: 'ApiConnection'
              inputs: {
                host: {
                  connection: {
                    name: '@parameters(\'$connections\')[\'servicebus\'][\'connectionId\']'
                  }
                }
                method: 'delete'
                path: '/@{encodeURIComponent(encodeURIComponent(\'${serviceBusTopic}\'))}/subscriptions/@{encodeURIComponent(\'${serviceBusSubscription}\')}/messages/@{encodeURIComponent(triggerOutputs()?[\'headers\']?[\'MessageId\'])}/lock/@{encodeURIComponent(triggerOutputs()?[\'headers\']?[\'LockToken\'])}'
              }
              runAfter: {
                'Move_Blob_To_Quarantine': ['Succeeded']
              }
            }
          }
          runAfter: {
            'Wait_For_Approval': ['Succeeded']
          }
        }
      }
    }
    parameters: {
      '$connections': {
        value: {
          servicebus: {
            connectionId: resourceId('Microsoft.Web/connections', serviceBusConnectionName)
            connectionName: serviceBusConnectionName
            id: resourceId('Microsoft.Web/connections', serviceBusConnectionName)
          }
          office365: {
            connectionId: resourceId('Microsoft.Web/connections', office365ConnectionName)
            connectionName: office365ConnectionName
            id: resourceId('Microsoft.Web/connections', office365ConnectionName)
          }
          azureblob: {
            connectionId: resourceId('Microsoft.Web/connections', blobStorageConnectionName)
            connectionName: blobStorageConnectionName
            id: resourceId('Microsoft.Web/connections', blobStorageConnectionName)
          }
        }
      }
    }
  }
}

// ═══════════════════════════════════════════════════════════════════
// Outputs
// ═══════════════════════════════════════════════════════════════════

@description('Resource ID of the Logic App')
output logicAppId string = logicApp.id

@description('Endpoint to trigger the Logic App')
output triggerUrl string = logicApp.listCallbackUrl().value

@description('Approval timeout setting')
output approvalTimeoutSetting string = approvalTimeout

targetScope = 'resourceGroup'

@minLength(1)
@maxLength(64)
@description('Name of the environment used to generate resource names.')
param environmentName string

@minLength(1)
@description('Primary location for all resources.')
param location string = resourceGroup().location

var resourceToken = toLower(uniqueString(resourceGroup().id, environmentName))
var tags = { 'azd-env-name': environmentName }

// Storage Account for Functions runtime
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: 'st${resourceToken}'
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: 'deploymentpackage'
}

// Application Insights
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: 'log-${resourceToken}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${resourceToken}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// Service Bus
resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: 'sb-${resourceToken}'
  location: location
  tags: tags
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
}

resource registrationsQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: 'registrations'
  properties: {
    maxDeliveryCount: 10
    deadLetteringOnMessageExpiration: true
    defaultMessageTimeToLive: 'P14D'
    lockDuration: 'PT5M'
  }
}

// Flex Consumption App Service Plan
resource functionPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'plan-${resourceToken}'
  location: location
  tags: tags
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true
  }
}

// Function App
resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: 'func-registrations-${resourceToken}'
  location: location
  tags: union(tags, { 'azd-service-name': 'registrations' })
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: functionPlan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storageAccount.properties.primaryEndpoints.blob}deploymentpackage'
          authentication: {
            type: 'SystemAssignedIdentity'
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 100
        instanceMemoryMB: 2048
      }
      runtime: {
        name: 'node'
        version: '20'
      }
    }
    siteConfig: {
      appSettings: [
        {
          name: 'AzureWebJobsStorage__blobServiceUri'
          value: storageAccount.properties.primaryEndpoints.blob
        }
        {
          name: 'AzureWebJobsStorage__queueServiceUri'
          value: storageAccount.properties.primaryEndpoints.queue
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'node'
        }
        {
          name: 'ServiceBusConnection__fullyQualifiedNamespace'
          value: '${serviceBusNamespace.name}.servicebus.windows.net'
        }
        {
          name: 'DATAVERSE_ENVIRONMENT_URL'
          value: 'https://trek-dev.crm6.dynamics.com'
        }
      ]
    }
  }
}

// Role: Storage Blob Data Owner (Function runtime needs this for Flex Consumption)
resource storageBlobOwnerRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionApp.id, 'Storage Blob Data Owner')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b')
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Role: Storage Queue Data Contributor (for Function host state)
resource storageQueueRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionApp.id, 'Storage Queue Data Contributor')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '974c5e8b-45b9-4653-ba55-5f855dd0fb88')
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Role: Service Bus Data Sender (HttpIngestFunction sends to queue)
resource serviceBusSenderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBusNamespace.id, functionApp.id, 'Azure Service Bus Data Sender')
  scope: serviceBusNamespace
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39')
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Role: Service Bus Data Receiver (ProcessRegistrationFunction consumes from queue)
resource serviceBusReceiverRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBusNamespace.id, functionApp.id, 'Azure Service Bus Data Receiver')
  scope: serviceBusNamespace
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0')
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Outputs consumed by azd
output AZURE_FUNCTION_APP_NAME string = functionApp.name
output AZURE_SERVICEBUS_NAMESPACE string = serviceBusNamespace.name
output FUNCTION_APP_URL string = 'https://${functionApp.properties.defaultHostName}'

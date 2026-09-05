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

// Storage Account for Functions runtime.
// Windows Consumption plans require key-based access (identity-based
// AzureWebJobsStorage is not supported outside Flex/Premium/Dedicated plans).
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: 'st${resourceToken}'
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
  }
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

// NOTE: The Windows Consumption (Y1) Function App + plan are intentionally
// NOT declared here. ARM's preflight "validate" step for the Dynamic SKU has
// a known, intermittent bug that reports SubscriptionIsOverQuotaForSku with
// "Current Limit: 0 / Amount required: 0" even though a live `az functionapp
// create` succeeds immediately with the same SKU in the same subscription —
// azd's provision flow always runs this validate step first and fails before
// ever attempting the real deployment. The postprovision hook
// (infra/postprovision.sh) creates the Function App + plan imperatively via
// `az functionapp create` instead, which uses a different, unaffected code
// path, then tags it azd-service-name=registrations so `azd deploy` can find it.

output AZURE_RESOURCE_GROUP string = resourceGroup().name
output AZURE_STORAGE_ACCOUNT_NAME string = storageAccount.name
output AZURE_SERVICEBUS_NAMESPACE string = serviceBusNamespace.name
output AZURE_APPINSIGHTS_NAME string = appInsights.name
output APPLICATIONINSIGHTS_CONNECTION_STRING string = appInsights.properties.ConnectionString

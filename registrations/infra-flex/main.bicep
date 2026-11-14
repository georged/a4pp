// Module 3 environment: the Flex Consumption function app and everything around it.
//
// This is a helper for setting up and resetting the environment. On camera the code is
// deployed from VS Code; this file only makes sure there is somewhere to deploy it to.
//
//   useManagedIdentity = false   Module 3 starting state: connection strings and keys
//   useManagedIdentity = true    Module 3 end state: identity-based queue and storage access
//
// Dataverse is not configured here. In the starting state, add the DataverseConnection app
// setting yourself (it holds a client secret, so it does not belong in a template). In the
// end state, the app's identity needs an application user in Dataverse.

targetScope = 'resourceGroup'

@description('Region for every resource. Australia East keeps the app next to Dataverse (crm6).')
param location string = resourceGroup().location

@description('false = connection strings (starting state). true = managed identity (end state).')
param useManagedIdentity bool = false

@description('Queue the functions read from and write to. Must match Queues.cs.')
param queueName string = 'registrations-flex'

var token = toLower(uniqueString(resourceGroup().id))
var tags = { course: 'azure-for-power-platform', module: 'm3' }
var deploymentContainerName = 'app-package'

// Built-in role definition IDs.
var storageBlobDataOwner = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
var serviceBusDataSender = '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
var serviceBusDataReceiver = '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'

// ---------- Storage (Functions runtime state and the deployment package) ----------

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'stflex${token}'
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    // Left on so the starting state works. Turning it off is the proof that nothing uses the key.
    allowSharedKeyAccess: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: deploymentContainerName
}

// ---------- Monitoring ----------

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-flex-${token}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-flex-${token}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ---------- Service Bus ----------

resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: 'sb-flex-${token}'
  location: location
  tags: tags
  sku: { name: 'Standard', tier: 'Standard' }
}

resource queue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBus
  name: queueName
  properties: {
    maxDeliveryCount: 10
    deadLetteringOnMessageExpiration: true
    defaultMessageTimeToLive: 'P14D'
    lockDuration: 'PT5M'
  }
}

// Starting state only: a key-based rule whose connection string goes into app settings.
resource sasRule 'Microsoft.ServiceBus/namespaces/authorizationRules@2022-10-01-preview' = if (!useManagedIdentity) {
  parent: serviceBus
  name: 'functions'
  properties: {
    rights: [ 'Listen', 'Send', 'Manage' ]
  }
}

// ---------- Function app on Flex Consumption ----------

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'plan-flex-${token}'
  location: location
  tags: tags
  kind: 'functionapp'
  sku: { tier: 'FlexConsumption', name: 'FC1' }
  properties: { reserved: true }
}

var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'

var commonSettings = [
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
]

var secretSettings = [
  { name: 'AzureWebJobsStorage', value: storageConnectionString }
  { name: 'DEPLOYMENT_STORAGE_CONNECTION_STRING', value: storageConnectionString }
  { name: 'ServiceBusConnection', value: useManagedIdentity ? '' : sasRule!.listKeys().primaryConnectionString }
]

var identitySettings = [
  { name: 'AzureWebJobsStorage__accountName', value: storage.name }
  { name: 'ServiceBusConnection__fullyQualifiedNamespace', value: '${serviceBus.name}.servicebus.windows.net' }
]

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: 'func-registrations-flex-${token}'
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentContainerName}'
          authentication: useManagedIdentity
            ? { type: 'SystemAssignedIdentity' }
            : {
                type: 'StorageAccountConnectionString'
                storageAccountConnectionStringName: 'DEPLOYMENT_STORAGE_CONNECTION_STRING'
              }
        }
      }
      runtime: { name: 'dotnet-isolated', version: '10.0' }
      scaleAndConcurrency: {
        maximumInstanceCount: 100
        instanceMemoryMB: 2048
      }
    }
    siteConfig: {
      appSettings: concat(commonSettings, useManagedIdentity ? identitySettings : secretSettings)
    }
  }
  dependsOn: [ deploymentContainer ]
}

// ---------- End state only: what the app's identity is allowed to do ----------

resource storageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (useManagedIdentity) {
  scope: storage
  name: guid(storage.id, functionApp.id, storageBlobDataOwner)
  properties: {
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataOwner)
  }
}

resource senderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (useManagedIdentity) {
  scope: serviceBus
  name: guid(serviceBus.id, functionApp.id, serviceBusDataSender)
  properties: {
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataSender)
  }
}

resource receiverRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (useManagedIdentity) {
  scope: serviceBus
  name: guid(serviceBus.id, functionApp.id, serviceBusDataReceiver)
  properties: {
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataReceiver)
  }
}

output functionAppName string = functionApp.name
output functionAppUrl string = 'https://${functionApp.properties.defaultHostName}'
output serviceBusNamespace string = serviceBus.name
output queueName string = queue.name
output storageAccountName string = storage.name
output managedIdentityPrincipalId string = functionApp.identity.principalId

targetScope = 'resourceGroup'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Short prefix used for generated resource names.')
param appName string = 'eris'

@description('Container App resource name.')
param containerAppName string = 'eris-api'

@secure()
@description('Admin key injected into the application configuration.')
param adminKey string

@description('Full container image reference. Leave empty to deploy infrastructure without the app revision.')
param containerImage string = ''

@description('Ephemeral SQLite path inside the container filesystem.')
param databasePath string = '/tmp/eris/eris.db'

@minValue(1)
@description('Minimum number of app replicas.')
param minReplicas int = 1

@minValue(1)
@description('Maximum number of app replicas.')
param maxReplicas int = 1

@minValue(1)
@description('CPU cores allocated to the container.')
param cpu int = 1

@description('Memory allocated to the container.')
param memory string = '2Gi'

@description('Tags applied to Azure resources.')
param tags object = {
  application: 'eris'
  environment: 'prod'
  managedBy: 'copilot'
}

var sanitizedAppName = toLower(replace(appName, '-', ''))
var suffix = toLower(take(uniqueString(subscription().subscriptionId, resourceGroup().id, appName), 6))
var acrName = take('${sanitizedAppName}${suffix}acr', 50)
var workspaceName = '${appName}-logs-${suffix}'
var managedEnvironmentName = '${appName}-env'
var deployContainerApp = !empty(containerImage)
var acrPullRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  sku: {
    name: 'Basic'
  }
  tags: tags
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: workspaceName
  location: location
  tags: tags
  properties: {
    features: {
      disableLocalAuth: false
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
    retentionInDays: 30
    sku: {
      name: 'PerGB2018'
    }
  }
}

resource managedEnvironment 'Microsoft.App/managedEnvironments@2024-10-02-preview' = {
  name: managedEnvironmentName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsWorkspace.properties.customerId
        sharedKey: logAnalyticsWorkspace.listKeys().primarySharedKey
      }
    }
    publicNetworkAccess: 'Enabled'
    zoneRedundant: false
  }
}

resource containerApp 'Microsoft.App/containerApps@2025-01-01' = if (deployContainerApp) {
  name: containerAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  tags: tags
  properties: {
    environmentId: managedEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        allowInsecure: false
        external: true
        targetPort: 8080
        transport: 'auto'
      }
      registries: [
        {
          identity: 'system'
          server: containerRegistry.properties.loginServer
        }
      ]
      secrets: [
        {
          name: 'admin-key'
          value: adminKey
        }
      ]
    }
    template: {
      containers: [
        {
          image: containerImage
          name: 'eris-api'
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'Admin__Key'
              secretRef: 'admin-key'
            }
            {
              name: 'ERIS_DB_PATH'
              value: databasePath
            }
          ]
          probes: [
            {
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 20
              timeoutSeconds: 5
              type: 'Liveness'
            }
            {
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 5
              periodSeconds: 10
              timeoutSeconds: 5
              type: 'Readiness'
            }
          ]
          resources: {
            cpu: cpu
            memory: memory
          }
        }
      ]
      scale: {
        maxReplicas: maxReplicas
        minReplicas: minReplicas
      }
    }
  }
}

resource acrPullAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployContainerApp) {
  name: guid(containerRegistry.id, containerApp!.id, acrPullRoleDefinitionId)
  scope: containerRegistry
  properties: {
    principalId: containerApp!.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: acrPullRoleDefinitionId
  }
}

output acrName string = containerRegistry.name
output acrLoginServer string = containerRegistry.properties.loginServer
output managedEnvironmentId string = managedEnvironment.id
output containerAppId string = deployContainerApp ? containerApp!.id : ''
output containerAppUrl string = deployContainerApp ? 'https://${containerApp!.properties.configuration.ingress.fqdn}' : ''
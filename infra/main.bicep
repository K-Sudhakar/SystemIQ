targetScope = 'resourceGroup'

@description('Short, globally unique workload prefix. Use lowercase letters and numbers only.')
@minLength(3)
@maxLength(12)
param namePrefix string

@allowed([
  'dev'
  'test'
  'prod'
])
param environmentName string

@description('Azure region approved for the workload and PHI data residency.')
param location string = resourceGroup().location

@description('Microsoft Entra tenant that issues API tokens.')
param entraTenantId string

@description('Client ID of the API app registration.')
param apiClientId string

@description('Azure OpenAI endpoint. The Azure OpenAI resource is managed outside this template.')
param azureOpenAiEndpoint string

@description('Existing Azure OpenAI account name.')
param azureOpenAiResourceName string

@description('Resource group containing the existing Azure OpenAI account.')
param azureOpenAiResourceGroupName string = resourceGroup().name

@description('Subscription containing the existing Azure OpenAI account.')
param azureOpenAiSubscriptionId string = subscription().subscriptionId

@description('Azure OpenAI chat-completion deployment name.')
param azureOpenAiChatDeployment string

@description('Azure OpenAI embedding deployment name.')
param azureOpenAiEmbeddingDeployment string = 'text-embedding-3-small'

@description('Retention for Application Insights and Log Analytics telemetry.')
@minValue(30)
@maxValue(730)
param telemetryRetentionInDays int = 90

@description('Rate-limit threshold for access denials.')
@minValue(1)
param rateLimitDenialCount int = 5

@description('Rolling rate-limit window in minutes.')
@minValue(1)
param rateLimitWindowMinutes int = 10

@description('Key Vault secret name containing the database connection catalog JSON.')
param databaseConnectionsSecretName string = 'database-connections'

@description('Key Vault secret name containing the DataIQ RBAC policy JSON.')
param accessPolicySecretName string = 'dataiq-access-policy'

@description('Deploy the Azure Static Web App. Disable when the frontend is hosted separately.')
param deployStaticWebApp bool = true

var workloadName = '${namePrefix}-${environmentName}'
var compactName = toLower(replace(workloadName, '-', ''))
var storageName = take('${compactName}${uniqueString(resourceGroup().id)}', 24)
var keyVaultName = take('${workloadName}-${uniqueString(resourceGroup().id)}', 24)
var functionAppName = take('${workloadName}-api-${uniqueString(resourceGroup().id)}', 60)
var staticWebAppName = '${workloadName}-web'
var planName = '${workloadName}-ep1'
var logAnalyticsName = '${workloadName}-logs'
var appInsightsName = '${workloadName}-appi'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    retentionInDays: telemetryRetentionInDays
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    RetentionInDays: telemetryRetentionInDays
    DisableIpMasking: false
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

resource azureOpenAi 'Microsoft.CognitiveServices/accounts@2023-05-01' existing = {
  name: azureOpenAiResourceName
  scope: resourceGroup(azureOpenAiSubscriptionId, azureOpenAiResourceGroupName)
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowCrossTenantReplication: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    isHnsEnabled: false
    minimumTlsVersion: 'TLS1_2'
    publicNetworkAccess: 'Enabled'
    supportsHttpsTrafficOnly: true
    encryption: {
      keySource: 'Microsoft.Storage'
      requireInfrastructureEncryption: true
      services: {
        blob: {
          enabled: true
          keyType: 'Account'
        }
        file: {
          enabled: true
          keyType: 'Account'
        }
      }
    }
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 30
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: 30
    }
  }
}

resource chatHistoryContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'chat-history'
  properties: {
    publicAccess: 'None'
  }
}

resource glossaryContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'glossary'
  properties: {
    publicAccess: 'None'
  }
}

resource feedbackContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'feedback'
  properties: {
    publicAccess: 'None'
  }
}

resource auditLogContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'audit-log'
  properties: {
    publicAccess: 'None'
  }
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource denialTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: tableService
  name: 'AccessDenials'
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: tenant().tenantId
    enableRbacAuthorization: true
    enablePurgeProtection: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
    sku: {
      family: 'A'
      name: 'standard'
    }
  }
}

resource functionPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  kind: 'elastic'
  sku: {
    name: 'EP1'
    tier: 'ElasticPremium'
    size: 'EP1'
    family: 'EP'
    capacity: 1
  }
  properties: {
    maximumElasticWorkerCount: 20
    reserved: true
    zoneRedundant: false
  }
}

resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = if (deployStaticWebApp) {
  name: staticWebAppName
  location: location
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    allowConfigFileUpdates: true
    stagingEnvironmentPolicy: 'Enabled'
  }
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: functionPlan.id
    clientAffinityEnabled: false
    httpsOnly: true
    publicNetworkAccess: 'Enabled'
    siteConfig: {
      alwaysOn: true
      ftpsState: 'Disabled'
      http20Enabled: true
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      linuxFxVersion: 'DOTNET-ISOLATED|9.0'
      use32BitWorkerProcess: false
      cors: {
        allowedOrigins: deployStaticWebApp
          ? [
              'https://${staticWebApp!.properties.defaultHostname}'
            ]
          : []
        supportCredentials: false
      }
    }
  }
}

resource functionAppSettings 'Microsoft.Web/sites/config@2023-12-01' = {
  parent: functionApp
  name: 'appsettings'
  properties: {
    APPLICATIONINSIGHTS_CONNECTION_STRING: appInsights.properties.ConnectionString
    ApplicationInsightsAgent_EXTENSION_VERSION: '~3'
    FUNCTIONS_EXTENSION_VERSION: '~4'
    FUNCTIONS_WORKER_RUNTIME: 'dotnet-isolated'
    WEBSITE_RUN_FROM_PACKAGE: '1'
    AzureWebJobsStorage__accountName: storage.name
    AzureWebJobsStorage__credential: 'managedidentity'
    CHAT_HISTORY_BLOB_CONTAINER_URI: 'https://${storage.name}.blob.${environment().suffixes.storage}/${chatHistoryContainer.name}'
    GLOSSARY_BLOB_CONTAINER_URI: 'https://${storage.name}.blob.${environment().suffixes.storage}/${glossaryContainer.name}'
    FEEDBACK_BLOB_CONTAINER_URI: 'https://${storage.name}.blob.${environment().suffixes.storage}/${feedbackContainer.name}'
    AUDIT_LOG_BLOB_CONTAINER_URI: 'https://${storage.name}.blob.${environment().suffixes.storage}/${auditLogContainer.name}'
    RATE_LIMIT_TABLE_ENDPOINT: 'https://${storage.name}.table.${environment().suffixes.storage}'
    RATE_LIMIT_TABLE_NAME: denialTable.name
    RATE_LIMIT_DENIAL_COUNT: string(rateLimitDenialCount)
    RATE_LIMIT_WINDOW_MINUTES: string(rateLimitWindowMinutes)
    AUTH_DISABLED: 'false'
    AZURE_AD_TENANT_ID: entraTenantId
    AZURE_AD_API_CLIENT_ID: apiClientId
    AZURE_OPENAI_ENDPOINT: azureOpenAiEndpoint
    AZURE_OPENAI_CHAT_DEPLOYMENT: azureOpenAiChatDeployment
    AZURE_OPENAI_EMBEDDING_DEPLOYMENT: azureOpenAiEmbeddingDeployment
    DATABASE_CONNECTIONS_JSON: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=${databaseConnectionsSecretName})'
    RBAC_POLICY_JSON: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=${accessPolicySecretName})'
  }
}

var keyVaultSecretsUserRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '4633458b-17de-408a-b874-0445c86b69e6'
)
var storageBlobDataContributorRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
)
var storageQueueDataContributorRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
)
var storageTableDataContributorRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
)
var cognitiveServicesOpenAiUserRoleId = subscriptionResourceId(
  azureOpenAiSubscriptionId,
  'Microsoft.Authorization/roleDefinitions',
  '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
)

resource keyVaultSecretsUserAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, functionApp.name, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: keyVaultSecretsUserRoleId
  }
}

resource storageBlobDataContributorAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionApp.name, storageBlobDataContributorRoleId)
  scope: storage
  properties: {
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: storageBlobDataContributorRoleId
  }
}

resource storageQueueDataContributorAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionApp.name, storageQueueDataContributorRoleId)
  scope: storage
  properties: {
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: storageQueueDataContributorRoleId
  }
}

resource storageTableDataContributorAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionApp.name, storageTableDataContributorRoleId)
  scope: storage
  properties: {
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: storageTableDataContributorRoleId
  }
}

module azureOpenAiUserAssignment 'modules/openai-role-assignment.bicep' = {
  name: 'openai-user-${uniqueString(azureOpenAi.id, functionApp.name)}'
  scope: resourceGroup(azureOpenAiSubscriptionId, azureOpenAiResourceGroupName)
  params: {
    accountName: azureOpenAiResourceName
    principalId: functionApp.identity.principalId
    stablePrincipalName: functionApp.name
    roleDefinitionId: cognitiveServicesOpenAiUserRoleId
  }
}

output functionAppName string = functionApp.name
output functionAppPrincipalId string = functionApp.identity.principalId
output functionApiBaseUrl string = 'https://${functionApp.properties.defaultHostName}'
output staticWebAppName string = deployStaticWebApp ? staticWebApp!.name : ''
output staticWebAppHostName string = deployStaticWebApp ? staticWebApp!.properties.defaultHostname : ''
output storageAccountName string = storage.name
output keyVaultName string = keyVault.name
output applicationInsightsName string = appInsights.name

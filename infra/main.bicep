// Harris County AI Document Review Assistant — infrastructure entry point.
//
// Deploys into an existing resource group (one resource group per
// environment, e.g. rg-harriscountyai-dev). Create the group first:
//   az group create -n rg-harriscountyai-<env> -l <region>
// then deploy:
//   az deployment group create -g rg-harriscountyai-<env> \
//     -f infra/main.bicep -p infra/main.bicepparam
//
// All resource names derive from baseName + environmentName so a second
// environment can be stood up by changing environmentName alone (the
// storage account name is the one exception; see storageAccountName).

targetScope = 'resourceGroup'

@description('Base name shared by all resources.')
param baseName string = 'harriscountyai'

@description('Environment name used as a suffix on every resource (dev, test, prod...).')
param environmentName string = 'dev'

@description('Region for all resources except the Static Web App.')
param location string = resourceGroup().location

@description('Region for the Static Web App (limited regional availability).')
param staticWebAppLocation string = 'eastus2'

@description('Storage account names cannot contain hyphens and must be globally unique, so the name is a parameter rather than derived. Defaults to a deterministic unique name for new environments.')
param storageAccountName string = 'stharrisai${uniqueString(resourceGroup().id)}'

@description('Administrator login for the SQL server.')
param sqlAdministratorLogin string

@description('Administrator password for the SQL server. Supply at deploy time; never committed.')
@secure()
param sqlAdministratorPassword string

module appInsights 'modules/app-insights.bicep' = {
  name: 'app-insights'
  params: {
    appInsightsName: 'appi-${baseName}-${environmentName}'
    logAnalyticsWorkspaceName: 'log-${baseName}-${environmentName}'
    location: location
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    storageAccountName: storageAccountName
    location: location
  }
}

module search 'modules/search.bicep' = {
  name: 'search'
  params: {
    searchServiceName: 'srch-${baseName}-${environmentName}'
    location: location
  }
}

module documentIntelligence 'modules/document-intelligence.bicep' = {
  name: 'document-intelligence'
  params: {
    accountName: 'di-${baseName}-${environmentName}'
    location: location
  }
}

module openAi 'modules/openai.bicep' = {
  name: 'openai'
  params: {
    accountName: 'aoai-${baseName}-${environmentName}'
    location: location
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    sqlServerName: 'sql-${baseName}-${environmentName}'
    location: location
    administratorLogin: sqlAdministratorLogin
    administratorLoginPassword: sqlAdministratorPassword
  }
}

module appService 'modules/app-service.bicep' = {
  name: 'app-service'
  params: {
    appServicePlanName: 'plan-${baseName}-${environmentName}'
    webAppName: 'app-${baseName}-${environmentName}'
    location: location
    appInsightsConnectionString: appInsights.outputs.connectionString
  }
}

module staticWebApp 'modules/static-web-app.bicep' = {
  name: 'static-web-app'
  params: {
    staticWebAppName: 'swa-${baseName}-${environmentName}'
    location: staticWebAppLocation
  }
}

// Outputs shaped for the backend's appsettings keys (see infra/README.md
// for the output -> appsettings mapping).
output databaseConnectionStringTemplate string = sql.outputs.connectionStringTemplate
output sqlServerFqdn string = sql.outputs.sqlServerFqdn
output databaseName string = sql.outputs.databaseName
output blobEndpoint string = storage.outputs.blobEndpoint
output storageAccountName string = storage.outputs.storageAccountName
output caseDocumentsContainerName string = storage.outputs.containerNames[0]
output knowledgeBaseContainerName string = storage.outputs.containerNames[1]
output searchEndpoint string = search.outputs.searchEndpoint
output documentIntelligenceEndpoint string = documentIntelligence.outputs.endpoint
output openAiEndpoint string = openAi.outputs.endpoint
output openAiChatDeploymentName string = openAi.outputs.chatDeploymentName
output openAiEmbeddingDeploymentName string = openAi.outputs.embeddingDeploymentName
output appInsightsConnectionString string = appInsights.outputs.connectionString
output backendUrl string = appService.outputs.webAppUrl
output frontendHostname string = staticWebApp.outputs.defaultHostname

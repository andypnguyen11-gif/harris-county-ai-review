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

// App Service and Azure SQL are the two resources a subscription is most
// likely to refuse outright, and they refuse per region: App Service plans
// count against a "Total VMs" quota that is zero in most regions on a Free
// Trial subscription, and Azure SQL reports ProvisioningDisabled in regions
// where the subscription is not permitted to create servers. Both refusals
// surface at preflight, before anything is created.
//
// They are separated from `location` so the compute tier can move to a region
// that permits it without relocating the AI services — which is not a free
// move, because Azure AI Search holds the ingested corpus and rebuilding it
// elsewhere means re-running extraction and embedding over every document.
// Keeping data in place and moving compute is the cheaper half.
//
// The cross-region hop costs a few milliseconds per call, which is not
// measurable against Document Intelligence extraction or model inference. The
// database is the chattiest dependency, so it stays with the App Service.
@description('Region for the App Service plan and Azure SQL. Defaults to `location`; override when the subscription cannot provision compute or SQL there.')
param computeLocation string = location

@description('Storage account names cannot contain hyphens and must be globally unique, so the name is a parameter rather than derived. Defaults to a deterministic unique name for new environments.')
param storageAccountName string = 'stharrisai${uniqueString(resourceGroup().id)}'

// Like the storage account, a SQL logical server name is globally unique —
// it becomes a public DNS name under database.windows.net — so it is a
// parameter rather than a derived value. A name is also claimed by a *failed*
// create and stays bound to that attempt's region afterwards, which makes a
// deployment that failed on a region restriction unable to retry elsewhere
// under the same name. Being able to override the name is what unblocks that.
@description('SQL logical server name (globally unique under database.windows.net). Defaults to the derived name.')
param sqlServerName string = 'sql-${baseName}-${environmentName}'

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
    sqlServerName: sqlServerName
    location: computeLocation
    administratorLogin: sqlAdministratorLogin
    administratorLoginPassword: sqlAdministratorPassword
  }
}

module appService 'modules/app-service.bicep' = {
  name: 'app-service'
  params: {
    appServicePlanName: 'plan-${baseName}-${environmentName}'
    webAppName: 'app-${baseName}-${environmentName}'
    location: computeLocation
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

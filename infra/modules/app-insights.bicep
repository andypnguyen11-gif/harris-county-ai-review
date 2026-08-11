// Workspace-based Application Insights for backend telemetry.
// Log Analytics pay-as-you-go with 30-day retention; ingestion below the
// free monthly allowance costs nothing at MVP traffic levels.

@description('Application Insights component name.')
param appInsightsName string

@description('Log Analytics workspace name.')
param logAnalyticsWorkspaceName string

@description('Azure region for both resources.')
param location string

@description('Log retention in days.')
param retentionInDays int = 30

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionInDays
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
    RetentionInDays: retentionInDays
  }
}

output appInsightsName string = appInsights.name
output connectionString string = appInsights.properties.ConnectionString
output logAnalyticsWorkspaceName string = logAnalyticsWorkspace.name

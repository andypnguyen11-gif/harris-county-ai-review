// App Service plan and web app for the ASP.NET Core backend API.
// F1 (Free) Linux plan keeps the dev environment at zero hosting cost.

@description('App Service plan name.')
param appServicePlanName string

@description('Web app name (globally unique within azurewebsites.net).')
param webAppName string

@description('Azure region for the plan and app.')
param location string

@description('App Service plan SKU. F1 is the free tier.')
param skuName string = 'F1'

@description('Linux runtime stack for the backend.')
param linuxFxVersion string = 'DOTNETCORE|10.0'

@description('Application Insights connection string to wire into the app. Empty disables the setting.')
param appInsightsConnectionString string = ''

resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: appServicePlanName
  location: location
  kind: 'linux'
  sku: {
    name: skuName
  }
  properties: {
    reserved: true
  }
}

resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: linuxFxVersion
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      http20Enabled: true
      appSettings: empty(appInsightsConnectionString)
        ? []
        : [
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: appInsightsConnectionString
            }
          ]
    }
  }
}

output appServicePlanName string = appServicePlan.name
output webAppName string = webApp.name
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'

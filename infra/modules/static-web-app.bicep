// Static Web App for the Angular frontend. Free tier.
// Note: Static Web Apps are only offered in a subset of regions
// (e.g. eastus2, centralus, westus2), so the location is parameterized
// separately from the rest of the resource group.

@description('Static Web App name.')
param staticWebAppName string

@description('Region for the Static Web App (must be a Static Web Apps region).')
param location string = 'eastus2'

@description('SKU. Free tier covers the internal MVP.')
param skuName string = 'Free'

resource staticWebApp 'Microsoft.Web/staticSites@2024-04-01' = {
  name: staticWebAppName
  location: location
  sku: {
    name: skuName
    tier: skuName
  }
  properties: {
    stagingEnvironmentPolicy: 'Enabled'
    allowConfigFileUpdates: true
  }
}

output staticWebAppName string = staticWebApp.name
output defaultHostname string = staticWebApp.properties.defaultHostname

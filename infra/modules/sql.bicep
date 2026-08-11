// Azure SQL logical server and database for application data.
// Basic tier (5 DTU, 2 GB) is the cheapest predictable-cost option (~$5/month).

@description('SQL logical server name (globally unique within database.windows.net).')
param sqlServerName string

@description('Database name.')
param databaseName string = 'HarrisCountyAI'

@description('Azure region for the server and database.')
param location string

@description('Administrator login name for the SQL server.')
param administratorLogin string

@description('Administrator password for the SQL server. Never stored in source control.')
@secure()
param administratorLoginPassword string

@description('Database SKU name.')
param skuName string = 'Basic'

@description('Database SKU tier.')
param skuTier string = 'Basic'

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorLoginPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// Allows connections from Azure services (e.g. the App Service backend)
// without opening the server to arbitrary internet addresses.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  sku: {
    name: skuName
    tier: skuTier
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 2147483648
    zoneRedundant: false
  }
}

output sqlServerName string = sqlServer.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = database.name
// Connection string template without the password; the password is supplied
// at deploy time via app configuration or a secret store, never from source.
output connectionStringTemplate string = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${database.name};User ID=${administratorLogin};Password=<from-secret-store>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'

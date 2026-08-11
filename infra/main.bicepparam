// Parameter values for the existing dev environment (rg-harriscountyai-dev).
//
// Secrets are read from environment variables at deploy time and are never
// stored in this repository:
//   export SQL_ADMIN_LOGIN=<login>
//   export SQL_ADMIN_PASSWORD=<password>

using 'main.bicep'

param baseName = 'harriscountyai'
param environmentName = 'dev'
param location = 'eastus'
param staticWebAppLocation = 'eastus2'

// The dev storage account was created by hand with a random suffix,
// so its exact name is pinned here.
param storageAccountName = 'stharrisaikbqbst'

param sqlAdministratorLogin = readEnvironmentVariable('SQL_ADMIN_LOGIN', 'harriscountyadmin')
param sqlAdministratorPassword = readEnvironmentVariable('SQL_ADMIN_PASSWORD', '')

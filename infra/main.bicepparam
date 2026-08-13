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

// The AI services, storage and search live in eastus. The App Service plan and
// SQL server cannot: this subscription is a Free Trial, which carries a
// "Total VMs" quota of 0 for App Service in eastus, eastus2, westus2 and
// westeurope, and refuses Azure SQL provisioning in those regions outright.
// centralus is the nearest region that permits both, confirmed with
// `az deployment group validate` before deploying.
//
// This is a property of the subscription, not of the application. On a
// pay-as-you-go subscription the restriction lifts and this can go back to
// `location`; re-probe before assuming either way.
param computeLocation = 'centralus'

// Suffixed with the region because the unsuffixed name is permanently bound to
// eastus: the first deployment attempt claimed it there and then failed on the
// region restriction above. Azure keeps the claim — ARM reports the name as
// existing in eastus while the server itself is absent from the resource group
// and its DNS name does not resolve — so the name cannot be recreated in
// centralus. The suffix is the escape from that, not a naming convention.
param sqlServerName = 'sql-harriscountyai-dev-cus'

// The dev storage account was created by hand with a random suffix,
// so its exact name is pinned here.
param storageAccountName = 'stharrisaikbqbst'

param sqlAdministratorLogin = readEnvironmentVariable('SQL_ADMIN_LOGIN', 'harriscountyadmin')
param sqlAdministratorPassword = readEnvironmentVariable('SQL_ADMIN_PASSWORD', '')

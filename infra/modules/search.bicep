// Azure AI Search service used for RAG over case documents and the
// Harris County reference corpus (indexed separately).

@description('Search service name (globally unique within search.windows.net).')
param searchServiceName string

@description('Azure region for the search service.')
param location string

@description('Search SKU. The free tier allows 3 indexes / 50 MB, enough for the MVP corpus.')
param skuName string = 'free'

resource searchService 'Microsoft.Search/searchServices@2025-05-01' = {
  name: searchServiceName
  location: location
  sku: {
    name: skuName
  }
  properties: {
    replicaCount: 1
    partitionCount: 1
    hostingMode: 'Default'
    computeType: 'Default'
    publicNetworkAccess: 'enabled'
    semanticSearch: 'free'
    disableLocalAuth: false
    authOptions: {
      aadOrApiKey: {
        aadAuthFailureMode: 'http401WithBearerChallenge'
      }
    }
    networkRuleSet: {
      bypass: 'None'
      ipRules: []
    }
    encryptionWithCmk: {
      enforcement: 'Unspecified'
    }
  }
}

output searchServiceName string = searchService.name
output searchEndpoint string = 'https://${searchService.name}.search.windows.net'

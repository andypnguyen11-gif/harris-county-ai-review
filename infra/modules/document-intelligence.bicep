// Azure AI Document Intelligence (Form Recognizer) account used to extract
// text and structure from uploaded case documents.

@description('Document Intelligence account name.')
param accountName string

@description('Azure region for the account.')
param location string

@description('SKU. F0 is the free tier (500 pages/month).')
param skuName string = 'F0'

resource documentIntelligence 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: accountName
  location: location
  kind: 'FormRecognizer'
  sku: {
    name: skuName
  }
  properties: {
    publicNetworkAccess: 'Enabled'
  }
}

output accountName string = documentIntelligence.name
// The platform assigns a unique endpoint host at create time
// (e.g. https://<name>-<suffix>.cognitiveservices.azure.com/), so the
// endpoint is read back from the resource rather than composed by hand.
output endpoint string = documentIntelligence.properties.endpoint

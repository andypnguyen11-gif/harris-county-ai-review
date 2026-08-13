// Azure AI Document Intelligence (Form Recognizer) account used to extract
// text and structure from uploaded case documents.

@description('Document Intelligence account name.')
param accountName string

@description('Azure region for the account.')
param location string

// S0 rather than the free tier, and this is not a performance preference.
// F0 reads only the first two pages of a document and returns success, so a
// 100-page regulation is extracted as two pages with no error anywhere: the
// document ingests "successfully" into a handful of chunks, retrieval quietly
// misses everything past page two, and the system answers questions about the
// corpus while blind to most of it. Measured on the Harris County floodplain
// regulations, F0 produced 3 chunks where S0 produced 138.
//
// Dropping back to F0 to save money therefore costs correctness silently
// rather than loudly, which is the worst way for it to fail.
@description('SKU. S0 is pay-per-page; F0 is free but truncates every document to two pages.')
param skuName string = 'S0'

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

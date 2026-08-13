// Azure OpenAI account with the two model deployments the application uses:
// a chat model for semantic evaluation / question answering and an embedding
// model for indexing content into Azure AI Search.

@description('Azure OpenAI account name.')
param accountName string

@description('Azure region for the account.')
param location string

@description('Account SKU. S0 is the only tier; cost is per-token usage.')
param skuName string = 'S0'

@description('Name of the chat model deployment.')
param chatDeploymentName string = 'chat'

@description('Chat model to deploy.')
param chatModelName string = 'gpt-5-mini'

@description('Chat model version.')
param chatModelVersion string = '2025-08-07'

@description('Name of the embedding model deployment.')
param embeddingDeploymentName string = 'embeddings'

@description('Embedding model to deploy.')
param embeddingModelName string = 'text-embedding-3-small'

@description('Embedding model version.')
param embeddingModelVersion string = '1'

// Capacity is a per-minute rate limit, not a spend commitment: a GlobalStandard
// deployment bills per token generated, so headroom that goes unused costs
// nothing. The two deployments are sized separately because they are throttled
// by completely different workloads, and a single shared value silently
// starves one of them.
@description('Chat throughput capacity, in thousands of tokens per minute. Sized for interactive question answering, where several reviewers can be waiting on answers at once and a 429 surfaces as a failed answer.')
param chatDeploymentCapacity int = 100

@description('Embedding throughput capacity, in thousands of tokens per minute. Sized for corpus ingestion, which embeds a whole document in tight batches and is the heaviest token burst the system produces.')
param embeddingDeploymentCapacity int = 250

resource openAi 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: accountName
  location: location
  kind: 'OpenAI'
  sku: {
    name: skuName
  }
  properties: {
    publicNetworkAccess: 'Enabled'
  }
}

resource chatDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAi
  name: chatDeploymentName
  sku: {
    name: 'GlobalStandard'
    capacity: chatDeploymentCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: chatModelName
      version: chatModelVersion
    }
    versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
    raiPolicyName: 'Microsoft.DefaultV2'
    currentCapacity: chatDeploymentCapacity
  }
}

// Deployments on the same account must be created sequentially.
resource embeddingDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAi
  name: embeddingDeploymentName
  sku: {
    name: 'GlobalStandard'
    capacity: embeddingDeploymentCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: embeddingModelName
      version: embeddingModelVersion
    }
    versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
    raiPolicyName: 'Microsoft.DefaultV2'
    currentCapacity: embeddingDeploymentCapacity
  }
  dependsOn: [
    chatDeployment
  ]
}

output accountName string = openAi.name
// The platform assigns a unique endpoint host at create time
// (e.g. https://<name>-<suffix>.openai.azure.com/), so the endpoint is read
// back from the resource rather than composed by hand.
output endpoint string = openAi.properties.endpoint
output chatDeploymentName string = chatDeployment.name
output embeddingDeploymentName string = embeddingDeployment.name

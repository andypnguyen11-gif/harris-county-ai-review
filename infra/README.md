# Infrastructure as Code

Bicep templates for every Azure resource the Harris County AI Document Review
Assistant uses. The templates mirror the deployed dev environment
(`rg-harriscountyai-dev`, East US) and are parameterized so a second
environment can be stood up by changing `environmentName`.

## Layout

```text
infra/
  main.bicep                        entry point (resource-group scope)
  main.bicepparam                   parameter values for the dev environment
  modules/
    storage.bicep                   Storage account + blob containers
    search.bicep                    Azure AI Search
    document-intelligence.bicep     Azure AI Document Intelligence
    openai.bicep                    Azure OpenAI + model deployments
    sql.bicep                       Azure SQL server + database
    app-service.bicep               App Service plan + backend web app
    static-web-app.bicep            Static Web App for the Angular frontend
    app-insights.bicep              Application Insights + Log Analytics
```

## Resource group structure

One resource group per environment holds everything:
`rg-harriscountyai-<env>` (e.g. `rg-harriscountyai-dev`). The templates
deploy at resource-group scope; create the group first.

## Deploying

```bash
# 1. Create the resource group (once per environment)
az group create -n rg-harriscountyai-dev -l eastus

# 2. Provide secrets via environment variables (never committed)
export SQL_ADMIN_LOGIN=<login>
export SQL_ADMIN_PASSWORD=<password>

# 3. Preview
az deployment group what-if \
  -g rg-harriscountyai-dev \
  -f infra/main.bicep \
  -p infra/main.bicepparam

# 4. Deploy
az deployment group create \
  -g rg-harriscountyai-dev \
  -f infra/main.bicep \
  -p infra/main.bicepparam
```

To stand up a second environment, create its resource group and override the
environment parameters:

```bash
az deployment group create \
  -g rg-harriscountyai-test \
  -f infra/main.bicep \
  -p infra/main.bicepparam \
  -p environmentName=test storageAccountName=stharrisaitest01
```

(Storage account names cannot contain hyphens and must be globally unique,
so the name is passed explicitly; the dev account name `stharrisaikbqbst`
is pinned in `main.bicepparam`. When `main.bicep` is deployed without any
parameter file, `storageAccountName` defaults to a deterministic unique
name derived from the resource group.)

## Cost profile

The templates deliberately use free or lowest-cost tiers:

| Resource | SKU / tier | Approx. cost |
| --- | --- | --- |
| Storage account | Standard_LRS, Hot | pennies/month at MVP volumes |
| Azure AI Search | `free` | $0 (3 indexes, 50 MB) |
| Document Intelligence | `S0` | ~$10 per 1,000 pages on `prebuilt-layout`; effectively a one-time cost for the corpus load |
| Azure OpenAI | `S0`, GlobalStandard deployments (chat 100K TPM, embeddings 250K TPM) | per-token usage only; capacity is a rate limit, not a commitment |
| Azure SQL | Basic (5 DTU, 2 GB) | ~$5/month |
| App Service plan | `F1` Linux | $0 (60 CPU-min/day) |
| Static Web App | `Free` | $0 |
| Application Insights / Log Analytics | PerGB2018, 30-day retention | $0 under the free ingestion allowance |

Free tiers carry hard limits (one free Search service per subscription; F1 App
Service has daily CPU quotas). They are appropriate for the dev environment,
not production load.

Document Intelligence is the one place the free tier is deliberately refused.
F0 silently truncates every document to two pages, so it does not cost less —
it costs correctness, and does so without an error to notice. See the comment
in `modules/document-intelligence.bicep`.

## Secrets — intentionally not committed

The repository contains **no** secret values. The following are supplied at
deploy time or read from Azure afterwards:

| Secret | Where it lives |
| --- | --- |
| SQL admin login/password | `SQL_ADMIN_LOGIN` / `SQL_ADMIN_PASSWORD` environment variables at deploy time (`main.bicepparam` reads them via `readEnvironmentVariable`) |
| Storage account key / connection string | `az storage account show-connection-string` |
| Search admin key | `az search admin-key show` |
| Document Intelligence key | `az cognitiveservices account keys list` |
| Azure OpenAI key | `az cognitiveservices account keys list` |

The deployment outputs a database connection string **template** with a
`<from-secret-store>` password placeholder; the real password must be
injected from a secret store (GitHub environment secrets, Key Vault, or App
Service configuration), never from source control.

## Outputs → appsettings mapping

`az deployment group show -g rg-harriscountyai-dev -n main --query properties.outputs`

| Bicep output | appsettings key | Notes |
| --- | --- | --- |
| `databaseConnectionStringTemplate` | `ConnectionStrings:Database` | replace the password placeholder from a secret store |
| `blobEndpoint` | `BlobStorage:ConnectionString` | use the account connection string (or the endpoint with managed identity) |
| `caseDocumentsContainerName` | `BlobStorage:CaseDocumentsContainerName` | `case-documents` |
| `knowledgeBaseContainerName` | `BlobStorage:KnowledgeBaseContainerName` | `knowledge-base` |
| `searchEndpoint` | `Search:Endpoint` | also set `Search:ApiKey`; `Search:IndexName` defaults to `harris-county-chunks` |
| `documentIntelligenceEndpoint` | `DocumentIntelligence:Endpoint` | also set `DocumentIntelligence:ApiKey` |
| `openAiEndpoint` | `LanguageModel:Endpoint` **and** `Embeddings:Endpoint` | one Azure OpenAI account backs both; each section takes its own key |
| `openAiChatDeploymentName` | `LanguageModel:Deployment` | `chat` (gpt-5-mini) |
| `openAiEmbeddingDeploymentName` | `Embeddings:Deployment` | `embeddings` (text-embedding-3-small) |
| `appInsightsConnectionString` | `APPLICATIONINSIGHTS_CONNECTION_STRING` | set on the web app by the template |
| `backendUrl` / `frontendHostname` | — | hosting endpoints for the deployment pipeline |

The Document Intelligence and Azure OpenAI endpoints include a
platform-generated unique suffix (e.g. `di-harriscountyai-dev-6adc4.…`), so
templates read them back from the resource instead of composing them from the
account name.

## Validation

```bash
./infra/validate.sh
```

Compiles every template (treating lint warnings as failures) and asserts the
tiers and capacities the deployed environment depends on — the Document
Intelligence SKU, and the two Azure OpenAI deployment capacities. CI runs the
same script on every pull request.

The assertions are there because compiling is not enough. These defaults drifted
from the live resources once already: the templates kept the free tiers they
were authored with, the resources were tuned by hand to make corpus ingestion
work, and a redeploy would have reverted them. That reversion compiles, deploys,
and returns HTTP 200 — it just truncates every document to two pages and
throttles embedding. Nothing but an explicit assertion catches it.

Beyond the script, changes are verified with `az deployment group what-if`
against the live resource group before any deployment; existing resources should
report `NoChange`. Read the what-if output rather than skimming it — a
`Modify` on a resource you did not intend to touch is the signal that the
templates have drifted from reality again.

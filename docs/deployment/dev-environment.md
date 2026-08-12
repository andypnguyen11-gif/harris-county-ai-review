# Deploying the development environment

`.github/workflows/deploy-dev.yml` deploys the API to Azure App Service, the
Angular app to Azure Static Web Apps, applies EF Core migrations to Azure SQL,
and smoke-tests the result.

> **Status: authored, never executed.** The workflow has not been run, and no
> Azure resources, GitHub secrets, GitHub environments or federated identity
> credentials have been created for it. Everything in
> [One-time operator setup](#one-time-operator-setup) is outstanding and must
> be completed by a human with subscription access before the first run. Treat
> the first run as the workflow's first test.

---

## How the deployment is triggered

| Trigger | When to use it |
| --- | --- |
| `workflow_dispatch` | The normal path. Actions → *Deploy (development)* → *Run workflow*. |
| Push of a `deploy-dev-*` tag | An explicit, auditable release marker: `git tag deploy-dev-2026-08-12 && git push origin deploy-dev-2026-08-12`. |

There is deliberately **no push-to-`main` trigger**. The dev environment runs on
a fixed Azure credit budget, the workflow mutates a live database, and the
deployed environment's authentication story is not yet safe for unattended
public exposure (see [Authentication](#authentication-in-a-deployed-environment)).
Deploying should be a decision, not a side effect of merging. If continuous
deployment is wanted later, add a `push: branches: [main]` trigger and keep the
environment approval as the gate.

The job targets the `development` GitHub Environment, so environment
protection rules (required reviewers, wait timers, branch restrictions) apply
to every run.

---

## One-time operator setup

Run these once, from a shell signed in to the target subscription
(`az login`). Replace every `<placeholder>`. **Do not commit any value these
commands print.**

### 1. Provision the Azure infrastructure

The workflow deploys *code and configuration only*. It never creates
infrastructure, and it fails fast with a pointer to `infra/README.md` if a
target resource is missing.

At the time this workflow was authored, `rg-harriscountyai-dev` contained only
the storage account, Document Intelligence, Azure AI Search and Azure OpenAI
accounts. The App Service, Azure SQL server, Static Web App and Application
Insights resources that this workflow deploys into **did not exist yet**, so
the Bicep deployment has to be completed first:

```bash
export SQL_ADMIN_LOGIN=<sql-admin-login>
export SQL_ADMIN_PASSWORD=<sql-admin-password>

az deployment group what-if \
  -g rg-harriscountyai-dev \
  -f infra/main.bicep \
  -p infra/main.bicepparam

az deployment group create \
  -g rg-harriscountyai-dev \
  -f infra/main.bicep \
  -p infra/main.bicepparam
```

See `infra/README.md` for what each module creates and what it costs.

### 2. Create the deployment identity (federated credentials, no secret)

The workflow authenticates with GitHub OIDC. No Azure client secret or
publish profile password is stored in GitHub.

```bash
# Create an app registration for the pipeline and capture its ids.
az ad app create --display-name "harriscountyai-github-deploy"

APP_ID=$(az ad app list --display-name "harriscountyai-github-deploy" --query "[0].appId" -o tsv)
az ad sp create --id "$APP_ID"

# Grant it just the target resource group.
az role assignment create \
  --assignee "$APP_ID" \
  --role Contributor \
  --scope "/subscriptions/<subscription-id>/resourceGroups/rg-harriscountyai-dev"

# Trust GitHub's OIDC issuer for this repository's `development` environment.
# The subject must match exactly: the deploy job declares
# `environment: development`, which produces this subject claim.
az ad app federated-credential create \
  --id "$APP_ID" \
  --parameters '{
    "name": "github-harriscountyai-development",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:<owner>/<repo>:environment:development",
    "audiences": ["api://AzureADTokenExchange"]
  }'
```

`<owner>/<repo>` is what `gh repo view --json nameWithOwner -q .nameWithOwner`
prints. Contributor on the resource group is required rather than a narrower
role because the workflow writes App Service application settings and CORS
configuration and creates/deletes a temporary SQL firewall rule.

### 3. Create the GitHub environment

```bash
gh api -X PUT "repos/<owner>/<repo>/environments/development"
```

Then, in the repository settings for the `development` environment, add
required reviewers if every deployment should be approved by a human.

### 4. Add environment secrets

Set on the `development` environment (Settings → Environments → development →
Environment secrets), **not** at repository scope.

| Secret | Where the value comes from |
| --- | --- |
| `AZURE_CLIENT_ID` | the `appId` of the app registration from step 2 |
| `AZURE_TENANT_ID` | `az account show --query tenantId -o tsv` |
| `AZURE_SUBSCRIPTION_ID` | `az account show --query id -o tsv` |
| `SQL_ADMIN_LOGIN` | the SQL admin login used in step 1 |
| `SQL_ADMIN_PASSWORD` | the SQL admin password used in step 1 |
| `BLOB_STORAGE_CONNECTION_STRING` | `az storage account show-connection-string -g rg-harriscountyai-dev -n <storage-account> --query connectionString -o tsv` |
| `DOCUMENT_INTELLIGENCE_API_KEY` | `az cognitiveservices account keys list -g rg-harriscountyai-dev -n di-harriscountyai-dev --query key1 -o tsv` |
| `LANGUAGE_MODEL_API_KEY` | `az cognitiveservices account keys list -g rg-harriscountyai-dev -n aoai-harriscountyai-dev --query key1 -o tsv` |
| `EMBEDDINGS_API_KEY` | same Azure OpenAI account as `LANGUAGE_MODEL_API_KEY` |
| `SEARCH_API_KEY` | `az search admin-key show -g rg-harriscountyai-dev --service-name srch-harriscountyai-dev --query primaryKey -o tsv` |
| `AZURE_STATIC_WEB_APPS_API_TOKEN` | `az staticwebapp secrets list -g rg-harriscountyai-dev -n swa-harriscountyai-dev --query properties.apiKey -o tsv` |
| `LOCAL_DEV_SIGNING_KEY` | only needed if you deliberately deploy `LocalDevelopment` auth; generate a fresh 32+ character random value, never reuse the one in `appsettings.Development.json` |

The API keys above are the resources' account keys. Replacing them with managed
identity and Key Vault references is the right next step; see
[Known gaps](#known-gaps).

### 5. Add environment variables

Non-secret configuration, set as environment **variables**:

| Variable | Value |
| --- | --- |
| `AUTHENTICATION_MODE` | `EntraId` or `LocalDevelopment` — **no default; the workflow refuses to run without it** |
| `ENTRA_AUTHORITY` | required for `EntraId`, e.g. `https://login.microsoftonline.com/<tenant-id>/v2.0` |
| `ENTRA_AUDIENCE` | required for `EntraId`: the API's application ID URI or client id |
| `DOCUMENT_INTELLIGENCE_ENDPOINT` | `az cognitiveservices account show -g rg-harriscountyai-dev -n di-harriscountyai-dev --query properties.endpoint -o tsv` |
| `LANGUAGE_MODEL_ENDPOINT` | `az cognitiveservices account show -g rg-harriscountyai-dev -n aoai-harriscountyai-dev --query properties.endpoint -o tsv` |
| `LANGUAGE_MODEL_DEPLOYMENT` | the Azure OpenAI chat deployment name (`chat` in the Bicep) |
| `EMBEDDINGS_ENDPOINT` | the same Azure OpenAI endpoint |
| `EMBEDDINGS_DEPLOYMENT` | the embedding deployment name (`embeddings` in the Bicep) |
| `SEARCH_ENDPOINT` | `https://srch-harriscountyai-dev.search.windows.net` |
| `SEARCH_INDEX_NAME` | `harris-county-chunks` |

Optional overrides, each defaulted in the workflow, needed only for a second
environment: `AZURE_RESOURCE_GROUP`, `AZURE_WEBAPP_NAME`,
`AZURE_STATIC_WEB_APP_NAME`, `AZURE_SQL_SERVER_NAME`, `AZURE_SQL_DATABASE_NAME`,
`BLOB_CASE_DOCUMENTS_CONTAINER`, `BLOB_KNOWLEDGE_BASE_CONTAINER`.

---

## What a run does, in order

1. **Guards.** Fails before touching Azure if any required secret or variable is
   missing, or if the authentication mode is unset, unsupported, or
   `LocalDevelopment` without an explicit acknowledgement.
2. **Signs in to Azure** with the federated credential.
3. **Verifies the target resources exist** and resolves the backend hostname,
   frontend hostname and SQL FQDN from Azure rather than assuming names.
4. **Publishes and zips the API** (`dotnet publish -c Release`).
5. **Generates an idempotent migration script** and uploads it as a build
   artifact, so the DDL applied to Azure SQL is reviewable after the fact.
6. **Opens a temporary SQL firewall rule** for the runner's public IP, applies
   the script with `sqlcmd`, and removes the rule in an `always()` cleanup step.
7. **Writes App Service application settings** from the environment secrets and
   variables.
8. **Adds the Static Web App origin to App Service CORS** (idempotent).
9. **Deploys the API**, then builds the Angular app against the resolved API URL
   and **deploys the frontend**.
10. **Smoke-tests** the deployment and fails the run if anything is wrong.

### Smoke test

| Check | Expected |
| --- | --- |
| `GET {backend}/health` (retried for up to ~5 minutes) | `200` with body `Healthy` |
| `POST {backend}/api/auth/dev-token` (skipped in `LocalDevelopment` mode) | `404` — the deployed API must not issue development tokens |
| `GET {backend}/api/cases` anonymously | `401` |
| `GET {frontend}/` | `200` |

The retry budget exists because the App Service plan is F1, which has no
Always On, so the first request after a deploy pays a cold start.

---

## Design decisions

### Migrations run as a discrete deploy step, not at startup

The application has a `Database:ApplyMigrationsAtStartup` setting, and it is
**not** used here. The workflow generates
`dotnet ef migrations script --idempotent` and applies it with `sqlcmd` before
the new code is deployed, because:

1. `Program.cs` only honours `Database:ApplyMigrationsAtStartup` inside
   `if (app.Environment.IsDevelopment())`. The deployed app runs as
   `Production`, so the setting is inert there and enabling it would require a
   source change that also weakens the local/deployed separation.
2. A failed startup migration on App Service presents as a crash-looping site
   with the failure buried in the log stream. As a pipeline step it fails
   loudly, with the SQL error in the run log, *before* new code reaches the app.
3. Startup migrations race whenever more than one instance boots.

The script is idempotent (every migration is guarded against
`__EFMigrationsHistory`), so re-running a deployment is safe. The workflow
explicitly sets `Database__ApplyMigrationsAtStartup=false` on the web app.

`dotnet ef database update` was rejected in favour of a generated script
because the repository's `DesignTimeDbContextFactory` hard-codes the local
SQL Server connection string; generating a script never opens a connection, so
there is no chance of a deploy-time command silently targeting the wrong
database.

### Federated identity instead of stored credentials

`azure/login@v2` with `permissions: id-token: write` exchanges a short-lived
GitHub OIDC token for an Azure token. There is no `AZURE_CREDENTIALS` service
principal password and no App Service publish profile in GitHub, so there is
nothing long-lived to leak or rotate. The federated credential is scoped to
this repository's `development` environment, so a workflow on a fork or on a
job without `environment: development` cannot obtain the credential.

### Configuration lives in Azure, not in the repository

`appsettings.json` in the repository carries empty-string placeholders. Every
deployed value is written to App Service application settings from GitHub
environment secrets and variables on each run, using the ASP.NET Core `__`
nesting convention (`Search__ApiKey` → `Search:ApiKey`). `az` calls that could
echo settings use `--output none`, and assembled secrets are passed through
`::add-mask::`.

Note that the API registers `BlobStorage`, `DocumentIntelligence`,
`LanguageModel`, `Search` and `Embeddings` options with `ValidateOnStart`. A
missing endpoint or key does not degrade a feature — it prevents the host from
starting, and `/health` never answers. That is why the workflow checks all of
them up front.

---

## Authentication in a deployed environment

**`Authentication:Mode=LocalDevelopment` is not appropriate for a deployed
environment.** In that mode the API registers `IDevTokenService` and
`POST /api/auth/dev-token` hands any anonymous caller a signed JWT carrying the
`Reviewer` or `Administrator` role. On a public `*.azurewebsites.net` URL that
is equivalent to no authentication at all.

The workflow therefore:

- refuses to deploy `LocalDevelopment` unless a `workflow_dispatch` run is
  started with `i_understand_local_development_auth_is_insecure=true` (a tag
  push can never set it);
- requires a dedicated `LOCAL_DEV_SIGNING_KEY` secret if that override is used,
  so the public signing key committed in `appsettings.Development.json` is
  never the one protecting a deployed host;
- emits a workflow warning and a banner in the run summary when the override is
  used;
- asserts in the smoke test that `POST /api/auth/dev-token` returns `404` in
  every other mode.

`EntraId` is the correct mode for a deployed environment — but see the gap
below before choosing it.

---

## Known gaps

- **The workflow has never been executed.** No run has validated the Azure CLI
  calls, the `sqlcmd` invocation, the App Service and Static Web Apps deploy
  actions, or the smoke test against a real environment.
- **`EntraId` mode leaves the UI unusable.** The Angular app's only sign-in path
  is `POST /api/auth/dev-token`; there is no Entra ID sign-in flow yet. With
  `AUTHENTICATION_MODE=EntraId` the deployed API is correct and reachable with
  a token obtained out of band, but nobody can sign in through the deployed
  frontend. Choosing `LocalDevelopment` makes the UI work and makes the
  environment effectively unauthenticated. There is no third option today; a
  frontend Entra ID sign-in flow is the fix.
- **Resource keys, not managed identity.** Storage, Search, Document
  Intelligence and Azure OpenAI are reached with account keys held as GitHub
  secrets and copied into App Service settings. Managed identity with Key Vault
  references removes the copies and the rotation burden.
- **SQL admin credentials are the application's credentials.** The app connects
  as the SQL server administrator. A least-privilege application login is the
  correct next step.
- **Free tiers.** F1 App Service (no Always On, daily CPU quota), free Azure AI
  Search and F0 Document Intelligence are dev-only capacity, not a production
  profile.
- **No teardown, rollback or slot swap.** Redeploying a previous commit is the
  only rollback, and migrations are not reversed.
- **Task plan path.** `Tasks.md` lists the infrastructure directory as
  `infrastructure/`; the Bicep templates actually live in `infra/`. This
  document and the workflow use the real path.

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| The run fails at *Verify the target resources exist* | The Bicep deployment has not been run; the App Service, SQL server or Static Web App does not exist. |
| `/health` never returns 200 | The host is crashing on boot. Almost always a missing or malformed `DocumentIntelligence`, `LanguageModel`, `Search`, `Embeddings` or `BlobStorage` setting failing `ValidateOnStart`, or an unreachable database. Check the App Service log stream: `az webapp log tail -g rg-harriscountyai-dev -n app-harriscountyai-dev`. |
| Migration step cannot reach the server | The temporary firewall rule was not created, or a previous run left one behind: `az sql server firewall-rule list -g rg-harriscountyai-dev -s sql-harriscountyai-dev`. |
| Frontend loads but API calls fail with a CORS error | The Static Web App hostname changed; re-run the deploy so the CORS step re-adds the current origin. |
| Login fails with `AADSTS70021` | The federated credential subject does not match `repo:<owner>/<repo>:environment:development`. |

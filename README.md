# Harris County AI Document Review Assistant

An internal web application to help Harris County reviewers analyze submitted documents: verify required information is present, flag incomplete or insufficient responses, and answer grounded questions using both case documents and official Harris County reference material.

The system compares **what the applicant submitted** against **what Harris County requires**, and presents findings in a reviewable, auditable format. The reviewer always makes the final decision.

## Guiding Principle

> Use deterministic software for deterministic work, retrieval for knowledge, and LLM reasoning only when semantic understanding adds value.

- Missing fields, signatures, invalid dates → deterministic validation
- County requirements → retrieval from a curated reference corpus (RAG)
- Whether an explanation satisfies a requirement → LLM semantic evaluation
- All AI answers are citation-backed; insufficient evidence returns an explicit "insufficient evidence" response

## Planned Stack

| Layer | Technology |
|---|---|
| Frontend | Angular |
| Backend | ASP.NET Core / C# |
| Database | SQL Server / Azure SQL |
| Search / RAG | Azure AI Search |
| Document extraction | Azure AI Document Intelligence |
| LLM | Azure-hosted LLM |
| Storage | Azure Blob Storage |

## Deployment

Azure resources are defined as Bicep templates in [`infra/`](infra/README.md).
Application deployment runs from GitHub Actions:
[`.github/workflows/deploy-dev.yml`](.github/workflows/deploy-dev.yml) publishes
the API to Azure App Service, applies EF Core migrations to Azure SQL, builds
and publishes the Angular app to Azure Static Web Apps, and smoke-tests the
result. It authenticates to Azure with GitHub OIDC federated credentials — no
Azure client secret or publish profile is stored in the repository, and all
deployed configuration comes from GitHub environment secrets and variables
written to App Service settings.

Deployments are deliberate: the workflow runs on manual dispatch or on a
`deploy-dev-*` tag, never automatically on a merge to `main`, and it targets the
`development` GitHub Environment so it can require approval.

**The workflow requires one-time operator setup — federated identity, a GitHub
environment, secrets and variables — and has not yet been run.** The full
runbook, the design decisions behind it, and its known gaps are in
[`docs/deployment/dev-environment.md`](docs/deployment/dev-environment.md).

> `Authentication:Mode=LocalDevelopment` issues signed Reviewer and
> Administrator tokens to anonymous callers and must not be used for a deployed
> environment. The deployment workflow refuses that mode unless a run explicitly
> acknowledges the risk.

## Status

Planning phase. See [`PRD.md`](PRD.md) for the product requirements and [`Tasks.md`](Tasks.md) for the PR-by-PR implementation plan.

# System Overview

The Harris County AI Document Review Assistant compares what an applicant submitted against what Harris County requires, and presents findings in a reviewable, auditable format. See [`PRD.md`](../../PRD.md) for the full requirements.

## Guiding Principle

> Use deterministic software for deterministic work, retrieval for knowledge, and LLM reasoning only when semantic understanding adds value.

## High-Level Architecture

```text
Angular (frontend/)
   │
   ▼
ASP.NET Core API (backend/)
   │
   ├── Azure Blob Storage ............ original documents
   ├── Azure AI Document Intelligence . OCR / extraction
   ├── Azure AI Search ................ keyword + vector + hybrid retrieval, semantic ranking
   ├── Azure-hosted LLM ............... semantic validation, grounded Q&A
   └── SQL Server / Azure SQL ......... cases, documents, validation results, metadata
```

## Backend Layering

The solution follows a lightweight Clean Architecture split (see `backend/HarrisCountyAI.slnx`):

| Project | Responsibility | May depend on |
|---|---|---|
| `HarrisCountyAI.Domain` | Entities, enums, value objects, validation primitives | nothing |
| `HarrisCountyAI.Application` | Use cases, service interfaces, orchestration | Domain |
| `HarrisCountyAI.Infrastructure` | EF Core persistence, Azure SDK implementations | Application, Domain |
| `HarrisCountyAI.Api` | Controllers, middleware, HTTP concerns | Application, Infrastructure |

These rules are enforced by `HarrisCountyAI.ArchitectureTests`, which fails the build if a layer gains a forbidden dependency.

All external AI/Azure services sit behind application-owned interfaces (`IDocumentStorageService`, `IDocumentExtractionService`, `ILanguageModelService`, `IRetrievalService`, …) with Azure implementations in Infrastructure. This keeps business logic testable with fakes and makes every external dependency replaceable.

## Knowledge Domains

The system maintains a strict separation between two knowledge domains:

- **Case evidence** — documents uploaded for a specific application. Retrieval is always filtered by `CaseId`.
- **County evidence** — the curated Harris County reference corpus.

Content from one must never contaminate the other's retrieval results.

Both domains are untrusted input as far as the model is concerned: applicants author the documents in one, and administrators ingest the other from external sources. See [`security.md`](security.md) for how uploaded documents and retrieved passages are isolated from the instruction channel.

## Local Development

```bash
docker compose up -d      # SQL Server (localhost:1433) + Azurite blob emulator (localhost:10000)

cd backend && dotnet build && dotnet test
cd frontend && npm install && npm start
```

Azure integrations are configuration-driven; local development uses Azurite and fakes until real Azure resources are wired via `appsettings`/environment configuration.

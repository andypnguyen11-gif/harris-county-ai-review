# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Harris County AI Document Review Assistant — an internal web application that compares what an applicant submitted against what Harris County requires, using deterministic validation, RAG over a curated reference corpus, and selective LLM reasoning. See `PRD.md` for full requirements and `Tasks.md` for the PR-by-PR implementation plan.

**Current state:** planning phase. Only `PRD.md` and `Tasks.md` exist; the codebase will be scaffolded per PR-01 in `Tasks.md`.

**Stack:** Angular frontend (`frontend/`), ASP.NET Core / C# backend (`backend/`), SQL Server / Azure SQL, Azure AI Search (RAG), Azure AI Document Intelligence (extraction), Azure-hosted LLM, Azure Blob Storage.

## Commands

Once scaffolded (per Tasks.md PR-01):

```bash
# Frontend (from frontend/)
npm install
npm run build
npm test                          # run Angular tests
npm test -- --include=**/foo.spec.ts   # single test file

# Backend (from backend/)
dotnet restore
dotnet build
dotnet test                       # all test projects
dotnet test --filter "FullyQualifiedName~CaseTests"   # single test class/method
```

## Architecture

Backend follows Clean Architecture with four projects under `backend/src/`:

- **HarrisCountyAI.Api** — controllers, middleware; no business logic.
- **HarrisCountyAI.Application** — use cases organized by feature (Cases, Documents, Validation, Search, QuestionAnswering, KnowledgeBase).
- **HarrisCountyAI.Domain** — entities, enums, value objects, validation rules; no external dependencies.
- **HarrisCountyAI.Infrastructure** — EF persistence, Azure integrations (BlobStorage, DocumentIntelligence, Search, LanguageModels), repositories.

Tests live in `backend/tests/` (UnitTests, IntegrationTests, ArchitectureTests). Frontend features live under `frontend/src/app/features/`, with cross-cutting code in `core/` and `shared/`.

### Core engineering principle

> Use deterministic software for deterministic work, retrieval for knowledge, and LLM reasoning only when semantic understanding adds value.

- Missing field, missing signature, invalid date → deterministic C# validation rules, never the LLM.
- "What does Harris County require?" → retrieval from the reference corpus.
- "Does this explanation satisfy the requirement?" → LLM semantic evaluation.
- AI answers must cite sources; when evidence is insufficient, return an insufficient-evidence response rather than inventing an answer.

Maintain a strict separation between **case-specific uploaded documents** and the **Harris County reference corpus** — they are indexed, retrieved, and cited separately.

## Local Development

```bash
docker compose up -d   # SQL Server 2022 (localhost:1433, sa / LocalDev!Passw0rd) + Azurite (localhost:10000)
```

Node.js is managed via nvm (default alias points to Node 22.23+; Angular CLI requires ≥ 22.22.3).

## Git and PR Workflow

- Branch naming: `feature/pr-XX-short-name` (e.g. `feature/pr-03-document-upload`).
- PR titles: `PR-XX: Description` (e.g. `PR-03: Add document upload pipeline`). PR descriptions include the reason for the change, testing performed, known limitations, and screenshots for UI changes.
- **Never add AI attribution anywhere**: no `Co-Authored-By` trailers, no "Generated with" lines, no tool references in commit messages, PR titles, or PR bodies.
- Merges to main are squash merges with a custom commit subject (no auto-appended `(#N)` references).
- **Commit messages must NOT reference PR numbers or task numbers.** Describe the change itself (e.g. `Add document upload validation`, not `PR-07: complete task 3`). PR/task references belong in the PR title and description only.
- **Every PR must include tests for the code it adds or changes.** Do not mark a PR's work complete until its tests are written and `dotnet test` (and `npm test` for frontend changes) pass. Each PR in `Tasks.md` lists its expected test coverage — new validation rules get a unit test per rule, Azure integrations get unit tests with mocked services, persistence changes get integration tests.
- Run the full build and test suite before declaring any PR done; the Definition of Done sections in `Tasks.md` define the exact commands per PR.

# Harris County AI Document Review Assistant

## PR-Based Engineering Task List

**Status:** Draft v1
**Source:** Harris County AI Document Review Assistant PRD
**Repository Strategy:** Monorepo
**Frontend:** Angular
**Backend:** ASP.NET Core / C#
**Cloud:** Microsoft Azure
**Primary Database:** SQL Server / Azure SQL
**Search / RAG:** Azure AI Search
**Document Processing:** Azure AI Document Intelligence
**LLM:** Azure-hosted LLM

---

# 1. Repository Structure

Recommended top-level structure:

```text
harris-county-ai-review/
│
├── README.md
├── .gitignore
├── .editorconfig
├── docker-compose.yml
├── Directory.Build.props
│
├── docs/
│   ├── architecture/
│   │   ├── system-overview.md
│   │   ├── rag-architecture.md
│   │   ├── security.md
│   │   └── decisions/
│   │       ├── ADR-001-monorepo.md
│   │       ├── ADR-002-azure-ai-search.md
│   │       └── ADR-003-deterministic-vs-ai-validation.md
│   │
│   ├── prd/
│   │   └── product-requirements.md
│   │
│   ├── api/
│   │   └── endpoints.md
│   │
│   └── evaluation/
│       └── evaluation-strategy.md
│
├── frontend/
│   ├── angular.json
│   ├── package.json
│   ├── tsconfig.json
│   ├── src/
│   │   ├── main.ts
│   │   ├── styles.scss
│   │   ├── environments/
│   │   │   ├── environment.ts
│   │   │   └── environment.development.ts
│   │   │
│   │   └── app/
│   │       ├── app.component.ts
│   │       ├── app.routes.ts
│   │       │
│   │       ├── core/
│   │       │   ├── auth/
│   │       │   ├── guards/
│   │       │   ├── interceptors/
│   │       │   ├── services/
│   │       │   └── models/
│   │       │
│   │       ├── shared/
│   │       │   ├── components/
│   │       │   ├── pipes/
│   │       │   └── models/
│   │       │
│   │       └── features/
│   │           ├── dashboard/
│   │           ├── cases/
│   │           ├── document-upload/
│   │           ├── document-viewer/
│   │           ├── validation-report/
│   │           ├── question-answering/
│   │           └── knowledge-base/
│   │
│   └── tests/
│
├── backend/
│   ├── HarrisCountyAI.sln
│   │
│   ├── src/
│   │   ├── HarrisCountyAI.Api/
│   │   │   ├── Controllers/
│   │   │   ├── Middleware/
│   │   │   ├── Extensions/
│   │   │   ├── Program.cs
│   │   │   ├── appsettings.json
│   │   │   └── appsettings.Development.json
│   │   │
│   │   ├── HarrisCountyAI.Application/
│   │   │   ├── Cases/
│   │   │   ├── Documents/
│   │   │   ├── Validation/
│   │   │   ├── Search/
│   │   │   ├── QuestionAnswering/
│   │   │   ├── KnowledgeBase/
│   │   │   └── Common/
│   │   │
│   │   ├── HarrisCountyAI.Domain/
│   │   │   ├── Entities/
│   │   │   ├── Enums/
│   │   │   ├── ValueObjects/
│   │   │   ├── Validation/
│   │   │   └── Common/
│   │   │
│   │   └── HarrisCountyAI.Infrastructure/
│   │       ├── Persistence/
│   │       ├── Azure/
│   │       │   ├── BlobStorage/
│   │       │   ├── DocumentIntelligence/
│   │       │   ├── Search/
│   │       │   └── LanguageModels/
│   │       ├── Repositories/
│   │       └── DependencyInjection.cs
│   │
│   └── tests/
│       ├── HarrisCountyAI.UnitTests/
│       ├── HarrisCountyAI.IntegrationTests/
│       └── HarrisCountyAI.ArchitectureTests/
│
├── evaluation/
│   ├── datasets/
│   │   ├── retrieval/
│   │   └── generation/
│   └── scripts/
│
└── infrastructure/
    ├── README.md
    ├── bicep/
    │   ├── main.bicep
    │   ├── storage.bicep
    │   ├── search.bicep
    │   ├── database.bicep
    │   └── app-service.bicep
    │
    └── scripts/
        ├── seed-local-db.ps1
        └── seed-corpus.ps1
```

---

# 2. Branch / PR Convention

Recommended branch naming:

```text
feature/pr-01-project-foundation
feature/pr-02-case-domain
feature/pr-03-document-upload
feature/pr-04-document-extraction
```

PR title format:

```text
PR-01: Initialize application foundation
PR-02: Add case and document domain models
PR-03: Add document upload pipeline
```

Each PR should contain:

* clear description
* reason for change
* screenshots when UI changes
* testing performed
* known limitations
* checklist
* related issue/task number

---

# PR-01 — Repository and Application Foundation

## Goal

Create the initial monorepo, Angular application, ASP.NET Core solution, project references, and development conventions.

## Checklist

* [ ] Create GitHub repository.
* [ ] Add root `.gitignore`.
* [ ] Add `.editorconfig`.
* [ ] Add root `README.md`.
* [ ] Create Angular frontend.
* [ ] Create ASP.NET Core solution.
* [ ] Create API project.
* [ ] Create Application project.
* [ ] Create Domain project.
* [ ] Create Infrastructure project.
* [ ] Create test projects.
* [ ] Configure project references.
* [ ] Enable nullable reference types.
* [ ] Enable implicit usings.
* [ ] Configure API Swagger/OpenAPI.
* [ ] Add basic health endpoint.
* [ ] Confirm frontend builds.
* [ ] Confirm backend builds.
* [ ] Add initial architecture documentation.

## Files Created / Updated

```text
README.md
.gitignore
.editorconfig
Directory.Build.props

frontend/
frontend/package.json
frontend/angular.json
frontend/src/app/app.routes.ts

backend/HarrisCountyAI.sln

backend/src/HarrisCountyAI.Api/
backend/src/HarrisCountyAI.Application/
backend/src/HarrisCountyAI.Domain/
backend/src/HarrisCountyAI.Infrastructure/

backend/tests/HarrisCountyAI.UnitTests/
backend/tests/HarrisCountyAI.IntegrationTests/

docs/architecture/system-overview.md
```

## Definition of Done

```text
npm install
npm run build
```

works.

And:

```text
dotnet restore
dotnet build
dotnet test
```

works.

---

# PR-02 — Core Domain: Cases and Documents

## Goal

Create the foundational domain entities around which the rest of the application operates.

## Domain Models

Create:

```text
Case
Document
DocumentProcessingStatus
CaseStatus
DocumentType
```

Possible `Case` properties:

```text
Id
CaseNumber
Name
WorkflowType
Status
CreatedAt
UpdatedAt
```

Possible `Document` properties:

```text
Id
CaseId
FileName
BlobPath
DocumentType
ProcessingStatus
CreatedAt
```

## Checklist

* [ ] Create `Case` entity.
* [ ] Create `Document` entity.
* [ ] Create `CaseStatus` enum.
* [ ] Create `DocumentProcessingStatus` enum.
* [ ] Create `DocumentType` enum.
* [ ] Define basic domain relationships.
* [ ] Create unit tests for domain models.
* [ ] Add basic validation where appropriate.

## Files

```text
backend/src/HarrisCountyAI.Domain/Entities/Case.cs

backend/src/HarrisCountyAI.Domain/Entities/Document.cs

backend/src/HarrisCountyAI.Domain/Enums/CaseStatus.cs

backend/src/HarrisCountyAI.Domain/Enums/DocumentProcessingStatus.cs

backend/src/HarrisCountyAI.Domain/Enums/DocumentType.cs

backend/tests/HarrisCountyAI.UnitTests/Domain/
```

---

# PR-03 — Database Persistence

## Goal

Persist cases, documents, and metadata using Entity Framework Core.

## Checklist

* [ ] Add Entity Framework Core packages.
* [ ] Add SQL Server provider.
* [ ] Create `ApplicationDbContext`.
* [ ] Configure `Case`.
* [ ] Configure `Document`.
* [ ] Create entity configurations.
* [ ] Add connection string configuration.
* [ ] Add first migration.
* [ ] Add database initialization for development.
* [ ] Add repository abstraction only where useful.
* [ ] Add integration test against database.
* [ ] Confirm relationships and cascade behavior.

## Files

```text
backend/src/HarrisCountyAI.Infrastructure/Persistence/ApplicationDbContext.cs

backend/src/HarrisCountyAI.Infrastructure/Persistence/Configurations/CaseConfiguration.cs

backend/src/HarrisCountyAI.Infrastructure/Persistence/Configurations/DocumentConfiguration.cs

backend/src/HarrisCountyAI.Infrastructure/Persistence/Migrations/

backend/src/HarrisCountyAI.Infrastructure/DependencyInjection.cs

backend/src/HarrisCountyAI.Api/appsettings.json

backend/src/HarrisCountyAI.Api/appsettings.Development.json

backend/tests/HarrisCountyAI.IntegrationTests/Persistence/
```

---

# PR-04 — Case Management API

## Goal

Allow users to create and retrieve document review cases.

## API Endpoints

```text
POST /api/cases

GET /api/cases

GET /api/cases/{id}

PATCH /api/cases/{id}
```

## Checklist

* [ ] Create case request DTO.
* [ ] Create case response DTO.
* [ ] Create `CaseService`.
* [ ] Implement create-case flow.
* [ ] Implement get-case flow.
* [ ] Implement list-cases flow.
* [ ] Implement update-case flow.
* [ ] Add controller.
* [ ] Add validation.
* [ ] Add API integration tests.
* [ ] Document endpoints.

## Files

```text
backend/src/HarrisCountyAI.Api/Controllers/CasesController.cs

backend/src/HarrisCountyAI.Application/Cases/CreateCase/
backend/src/HarrisCountyAI.Application/Cases/GetCase/
backend/src/HarrisCountyAI.Application/Cases/GetCases/
backend/src/HarrisCountyAI.Application/Cases/UpdateCase/

backend/src/HarrisCountyAI.Application/Cases/CaseDto.cs

docs/api/endpoints.md
```

---

# PR-05 — Angular Shell and Case Dashboard

## Goal

Build the first usable frontend experience.

## UI

Create:

```text
Dashboard

Cases List

Create Case

Case Details
```

## Checklist

* [ ] Add application layout.
* [ ] Add top navigation.
* [ ] Add Angular routing.
* [ ] Add dashboard page.
* [ ] Add cases page.
* [ ] Add create-case form.
* [ ] Add case-details page.
* [ ] Create `CaseService`.
* [ ] Create TypeScript API models.
* [ ] Connect frontend to backend.
* [ ] Add loading states.
* [ ] Add empty states.
* [ ] Add basic error handling.

## Files

```text
frontend/src/app/app.routes.ts

frontend/src/app/core/services/case.service.ts

frontend/src/app/core/models/case.model.ts

frontend/src/app/features/dashboard/

frontend/src/app/features/cases/case-list/

frontend/src/app/features/cases/case-create/

frontend/src/app/features/cases/case-detail/

frontend/src/environments/environment.ts
```

---

# PR-06 — Azure Blob Storage Integration

## Goal

Allow documents to be securely uploaded and stored outside the SQL database.

## Checklist

* [ ] Create Azure Storage account.
* [ ] Create document container.
* [ ] Add Azure Blob SDK.
* [ ] Define `IDocumentStorageService`.
* [ ] Implement `AzureBlobDocumentStorageService`.
* [ ] Store Blob URI/path with document record.
* [ ] Add upload limits.
* [ ] Validate allowed MIME types.
* [ ] Validate allowed file extensions.
* [ ] Add storage configuration.
* [ ] Add unit tests with mocked service.
* [ ] Add integration test where practical.

## Files

```text
backend/src/HarrisCountyAI.Application/Documents/IDocumentStorageService.cs

backend/src/HarrisCountyAI.Infrastructure/Azure/BlobStorage/AzureBlobDocumentStorageService.cs

backend/src/HarrisCountyAI.Infrastructure/Azure/BlobStorage/BlobStorageOptions.cs

backend/src/HarrisCountyAI.Infrastructure/DependencyInjection.cs

backend/src/HarrisCountyAI.Api/appsettings.json
```

---

# PR-07 — Document Upload API

## Goal

Connect case records to real uploaded PDFs.

## API

```text
POST /api/cases/{caseId}/documents

GET /api/cases/{caseId}/documents

GET /api/cases/{caseId}/documents/{documentId}
```

## Checklist

* [ ] Create upload endpoint.
* [ ] Validate case exists.
* [ ] Validate file.
* [ ] Upload to Blob Storage.
* [ ] Create `Document` database record.
* [ ] Return document metadata.
* [ ] Add list-documents endpoint.
* [ ] Add get-document endpoint.
* [ ] Add upload error handling.
* [ ] Add tests.

## Files

```text
backend/src/HarrisCountyAI.Api/Controllers/DocumentsController.cs

backend/src/HarrisCountyAI.Application/Documents/UploadDocument/

backend/src/HarrisCountyAI.Application/Documents/GetDocuments/

backend/src/HarrisCountyAI.Application/Documents/GetDocument/
```

---

# PR-08 — Angular Document Upload

## Goal

Allow users to upload case documents from the browser.

## Checklist

* [ ] Create document upload component.
* [ ] Add file selector.
* [ ] Add drag-and-drop support.
* [ ] Support multiple PDFs.
* [ ] Display upload progress.
* [ ] Display upload failures.
* [ ] Show uploaded document list.
* [ ] Add status badges.
* [ ] Connect upload to active Case ID.

## Files

```text
frontend/src/app/features/document-upload/

frontend/src/app/core/services/document.service.ts

frontend/src/app/core/models/document.model.ts

frontend/src/app/features/cases/case-detail/
```

---

# PR-09 — Azure Document Intelligence Integration

## Goal

Extract text and document structure from uploaded PDFs.

This follows the project principle that the LLM should not be used as the OCR engine.

## Checklist

* [ ] Provision Azure Document Intelligence resource.
* [ ] Add SDK/configuration.
* [ ] Define `IDocumentExtractionService`.
* [ ] Implement Azure extraction service.
* [ ] Read files from Blob Storage.
* [ ] Submit documents for analysis.
* [ ] Extract page text.
* [ ] Extract paragraphs.
* [ ] Extract key/value pairs.
* [ ] Extract selection marks.
* [ ] Extract tables where available.
* [ ] Persist processing status.
* [ ] Handle Azure failures.
* [ ] Log processing duration.
* [ ] Add tests around mapping.

## Files

```text
backend/src/HarrisCountyAI.Application/Documents/Extraction/IDocumentExtractionService.cs

backend/src/HarrisCountyAI.Infrastructure/Azure/DocumentIntelligence/AzureDocumentExtractionService.cs

backend/src/HarrisCountyAI.Infrastructure/Azure/DocumentIntelligence/DocumentIntelligenceOptions.cs

backend/src/HarrisCountyAI.Application/Documents/Extraction/ExtractedDocument.cs

backend/src/HarrisCountyAI.Application/Documents/Extraction/ExtractedPage.cs

backend/src/HarrisCountyAI.Application/Documents/Extraction/ExtractedField.cs
```

---

# PR-10 — Document Normalization Layer

## Goal

Prevent the rest of the codebase from depending directly on Azure Document Intelligence output.

## Checklist

* [ ] Create normalized document model.
* [ ] Create normalized page model.
* [ ] Create normalized field model.
* [ ] Create normalization service.
* [ ] Map Azure extraction output.
* [ ] Preserve page references.
* [ ] Preserve confidence where useful.
* [ ] Persist normalized output.
* [ ] Add normalization unit tests.

## Files

```text
backend/src/HarrisCountyAI.Domain/Entities/NormalizedDocument.cs

backend/src/HarrisCountyAI.Domain/Entities/DocumentPage.cs

backend/src/HarrisCountyAI.Domain/Entities/DocumentField.cs

backend/src/HarrisCountyAI.Application/Documents/Normalization/IDocumentNormalizationService.cs

backend/src/HarrisCountyAI.Application/Documents/Normalization/DocumentNormalizationService.cs

backend/tests/HarrisCountyAI.UnitTests/Documents/Normalization/
```

---

# PR-11 — Deterministic Validation Framework

## Goal

Build the validation engine before introducing AI-based validation.

## Create Base Rule

```csharp
public interface IValidationRule
{
    Task<ValidationResult> ValidateAsync(
        ValidationContext context,
        CancellationToken cancellationToken);
}
```

## Initial Rules

```text
RequiredFieldRule

RequiredDocumentRule

DateRule

SignatureRule

CheckboxRule
```

## Checklist

* [ ] Create validation result entity.
* [ ] Create validation status enum.
* [ ] Create validation type enum.
* [ ] Create rule interface.
* [ ] Create validation context.
* [ ] Implement required-field rule.
* [ ] Implement required-document rule.
* [ ] Implement date rule.
* [ ] Implement signature rule.
* [ ] Implement checkbox rule.
* [ ] Implement validation orchestrator.
* [ ] Persist validation results.
* [ ] Add unit tests for every rule.

## Files

```text
backend/src/HarrisCountyAI.Domain/Validation/ValidationResult.cs

backend/src/HarrisCountyAI.Domain/Validation/ValidationContext.cs

backend/src/HarrisCountyAI.Domain/Enums/ValidationStatus.cs

backend/src/HarrisCountyAI.Domain/Enums/ValidationType.cs

backend/src/HarrisCountyAI.Application/Validation/IValidationRule.cs

backend/src/HarrisCountyAI.Application/Validation/DocumentValidationService.cs

backend/src/HarrisCountyAI.Application/Validation/Rules/RequiredFieldRule.cs

backend/src/HarrisCountyAI.Application/Validation/Rules/RequiredDocumentRule.cs

backend/src/HarrisCountyAI.Application/Validation/Rules/DateRule.cs

backend/src/HarrisCountyAI.Application/Validation/Rules/SignatureRule.cs

backend/src/HarrisCountyAI.Application/Validation/Rules/CheckboxRule.cs
```

---

# PR-12 — Workflow-Specific Requirements

## Goal

Define the actual Harris County MVP workflow.

Do not attempt to create a generic rule engine for every possible county workflow yet.

## Checklist

* [ ] Select initial permit/application type.
* [ ] Identify required documents.
* [ ] Identify required fields.
* [ ] Identify required signatures.
* [ ] Identify required dates.
* [ ] Identify required checkboxes.
* [ ] Create workflow definition.
* [ ] Map validation rules to workflow.
* [ ] Create sample valid package.
* [ ] Create sample incomplete package.
* [ ] Create sample invalid package.
* [ ] Add workflow validation tests.

## Files

```text
backend/src/HarrisCountyAI.Application/Validation/Workflows/

backend/src/HarrisCountyAI.Application/Validation/Workflows/InitialPermitWorkflow.cs

backend/tests/HarrisCountyAI.UnitTests/Validation/Workflows/

docs/architecture/initial-workflow.md
```

---

# PR-13 — Validation Report API

## Goal

Expose validation results to the frontend.

## API

```text
POST /api/cases/{caseId}/validate

GET /api/cases/{caseId}/validation
```

## Checklist

* [ ] Add validation endpoint.
* [ ] Execute configured rules.
* [ ] Persist results.
* [ ] Return grouped result.
* [ ] Include document reference.
* [ ] Include page number.
* [ ] Include extracted value.
* [ ] Include validation type.
* [ ] Add tests.

## Files

```text
backend/src/HarrisCountyAI.Api/Controllers/ValidationController.cs

backend/src/HarrisCountyAI.Application/Validation/RunValidation/

backend/src/HarrisCountyAI.Application/Validation/GetValidationReport/
```

---

# PR-14 — Validation Report UI

## Goal

Make document review results usable by a reviewer.

## UI Example

```text
Applicant Name
✓ Complete
John Smith

Owner Signature
✗ Missing

Reason for Exception
⚠ Needs Human Review
```

## Checklist

* [ ] Create validation report page.
* [ ] Group results by document/requirement.
* [ ] Add status indicators.
* [ ] Display extracted value.
* [ ] Display page references.
* [ ] Distinguish deterministic and AI results.
* [ ] Add loading state.
* [ ] Add validation rerun action.
* [ ] Add empty/error states.

## Files

```text
frontend/src/app/features/validation-report/

frontend/src/app/core/services/validation.service.ts

frontend/src/app/core/models/validation.model.ts

frontend/src/app/features/cases/case-detail/
```

---

# PR-15 — Azure LLM Abstraction

## Goal

Introduce the LLM without coupling the entire application to a specific model provider.

## Interface

```csharp
public interface ILanguageModelService
{
    Task<ModelResponse> GenerateAsync(
        ModelRequest request,
        CancellationToken cancellationToken);
}
```

## Checklist

* [ ] Create model request.
* [ ] Create model response.
* [ ] Define `ILanguageModelService`.
* [ ] Create Azure implementation.
* [ ] Add Azure endpoint configuration.
* [ ] Add deployment/model configuration.
* [ ] Add request timeout.
* [ ] Add cancellation handling.
* [ ] Add structured logging.
* [ ] Add token usage capture if available.
* [ ] Add fake implementation for unit tests.

## Files

```text
backend/src/HarrisCountyAI.Application/Common/AI/ILanguageModelService.cs

backend/src/HarrisCountyAI.Application/Common/AI/ModelRequest.cs

backend/src/HarrisCountyAI.Application/Common/AI/ModelResponse.cs

backend/src/HarrisCountyAI.Infrastructure/Azure/LanguageModels/AzureLanguageModelService.cs

backend/src/HarrisCountyAI.Infrastructure/Azure/LanguageModels/LanguageModelOptions.cs

backend/tests/HarrisCountyAI.UnitTests/Common/AI/FakeLanguageModelService.cs
```

---

# PR-16 — Semantic Validation

## Goal

Use the LLM only for fields where semantic understanding is required.

## Checklist

* [ ] Define semantic validation input.
* [ ] Define structured result schema.
* [ ] Create semantic validation prompt.
* [ ] Add prompt versioning.
* [ ] Implement semantic validation service.
* [ ] Validate model JSON output.
* [ ] Handle malformed model responses.
* [ ] Add `NeedsHumanReview` fallback.
* [ ] Configure which fields require semantic validation.
* [ ] Add first semantic validation rule.
* [ ] Add tests with fake model.
* [ ] Display semantic validation in existing report.

## Files

```text
backend/src/HarrisCountyAI.Application/Validation/Semantic/ISemanticValidationService.cs

backend/src/HarrisCountyAI.Application/Validation/Semantic/SemanticValidationService.cs

backend/src/HarrisCountyAI.Application/Validation/Semantic/SemanticValidationRequest.cs

backend/src/HarrisCountyAI.Application/Validation/Semantic/SemanticValidationResult.cs

backend/src/HarrisCountyAI.Application/Validation/Semantic/Prompts/SemanticValidationPrompt.cs
```

---

# PR-17 — Knowledge Base Domain and Administration API

## Goal

Introduce the Harris County reference corpus.

## Data Model

```text
KnowledgeDocument

Department

DocumentType

PermitType

Version

EffectiveDate

SourceUrl

IngestionDate

Status
```

## Checklist

* [ ] Add knowledge document entity.
* [ ] Add metadata fields.
* [ ] Add database migration.
* [ ] Create upload endpoint.
* [ ] Create list endpoint.
* [ ] Create delete/deactivate endpoint.
* [ ] Add admin-only service boundary.
* [ ] Store file in Blob Storage.
* [ ] Add tests.

## Files

```text
backend/src/HarrisCountyAI.Domain/Entities/KnowledgeDocument.cs

backend/src/HarrisCountyAI.Infrastructure/Persistence/Configurations/KnowledgeDocumentConfiguration.cs

backend/src/HarrisCountyAI.Application/KnowledgeBase/

backend/src/HarrisCountyAI.Api/Controllers/KnowledgeBaseController.cs
```

---

# PR-18 — Knowledge Base Admin UI

## Goal

Allow an administrator to manage the reference corpus.

## Checklist

* [ ] Create knowledge base page.
* [ ] Display existing documents.
* [ ] Upload reference document.
* [ ] Add department field.
* [ ] Add permit-type field.
* [ ] Add effective-date field.
* [ ] Add source URL field.
* [ ] Add status.
* [ ] Display ingestion status.
* [ ] Allow document deactivation/removal.

## Files

```text
frontend/src/app/features/knowledge-base/

frontend/src/app/core/services/knowledge-base.service.ts

frontend/src/app/core/models/knowledge-document.model.ts
```

---

# PR-19 — Document Chunking

## Goal

Turn reference documents into searchable units.

Avoid blindly splitting every document every 500 characters.

Prefer preserving document structure.

## Checklist

* [ ] Define chunk model.
* [ ] Define `IDocumentChunkingService`.
* [ ] Implement initial chunking strategy.
* [ ] Preserve heading.
* [ ] Preserve section.
* [ ] Preserve page.
* [ ] Preserve parent document.
* [ ] Add overlap only where needed.
* [ ] Add configurable max chunk size.
* [ ] Add chunking tests.
* [ ] Test against real county PDFs.

## Files

```text
backend/src/HarrisCountyAI.Application/Search/Chunking/DocumentChunk.cs

backend/src/HarrisCountyAI.Application/Search/Chunking/IDocumentChunkingService.cs

backend/src/HarrisCountyAI.Application/Search/Chunking/StructureAwareChunkingService.cs

backend/tests/HarrisCountyAI.UnitTests/Search/Chunking/
```

---

# PR-20 — Embedding Service

## Goal

Generate vector embeddings for document chunks.

## Checklist

* [ ] Define `IEmbeddingService`.
* [ ] Add Azure embedding implementation.
* [ ] Configure embedding deployment.
* [ ] Generate embedding per chunk.
* [ ] Batch requests where possible.
* [ ] Add retry policy.
* [ ] Log embedding failures.
* [ ] Capture model/version metadata.
* [ ] Add unit tests.

## Files

```text
backend/src/HarrisCountyAI.Application/Search/Embeddings/IEmbeddingService.cs

backend/src/HarrisCountyAI.Infrastructure/Azure/LanguageModels/AzureEmbeddingService.cs

backend/src/HarrisCountyAI.Infrastructure/Azure/LanguageModels/EmbeddingOptions.cs
```

---

# PR-21 — Azure AI Search Index

## Goal

Create the searchable corpus.

## Index Fields

Example:

```text
chunkId
documentId
sourceType
title
department
permitType
documentType
section
page
effectiveDate
sourceUrl
text
embedding
caseId
```

## Checklist

* [ ] Provision Azure AI Search.
* [ ] Design search index schema.
* [ ] Create vector field.
* [ ] Configure vector profile.
* [ ] Configure searchable text.
* [ ] Configure filterable metadata.
* [ ] Define `IDocumentIndexService`.
* [ ] Add index-document implementation.
* [ ] Add delete-document support.
* [ ] Add reindex support.
* [ ] Add tests.
* [ ] Document index design.

## Files

```text
backend/src/HarrisCountyAI.Application/Search/Indexing/IDocumentIndexService.cs

backend/src/HarrisCountyAI.Infrastructure/Azure/Search/AzureDocumentIndexService.cs

backend/src/HarrisCountyAI.Infrastructure/Azure/Search/SearchOptions.cs

backend/src/HarrisCountyAI.Infrastructure/Azure/Search/SearchIndexDefinition.cs

docs/architecture/rag-architecture.md
```

---

# PR-22 — Knowledge Corpus Ingestion Pipeline

## Goal

Connect all corpus-processing components.

## Pipeline

```text
Upload
↓
Extract
↓
Normalize
↓
Chunk
↓
Embed
↓
Index
```

## Checklist

* [ ] Create ingestion orchestrator.
* [ ] Run extraction.
* [ ] Run normalization.
* [ ] Run chunking.
* [ ] Run embeddings.
* [ ] Index chunks.
* [ ] Update ingestion status.
* [ ] Handle partial failure.
* [ ] Support reprocessing.
* [ ] Log processing stages.
* [ ] Add end-to-end integration test.

## Files

```text
backend/src/HarrisCountyAI.Application/KnowledgeBase/Ingestion/KnowledgeDocumentIngestionService.cs

backend/src/HarrisCountyAI.Application/KnowledgeBase/Ingestion/IngestionResult.cs

backend/src/HarrisCountyAI.Application/KnowledgeBase/Ingestion/IngestionStatus.cs
```

---

# PR-23 — Basic Retrieval Service

## Goal

Retrieve relevant Harris County reference passages.

## Checklist

* [ ] Define search request.
* [ ] Define search result.
* [ ] Define `IRetrievalService`.
* [ ] Implement vector search.
* [ ] Retrieve Top K.
* [ ] Include metadata.
* [ ] Return retrieval scores.
* [ ] Add metadata filtering.
* [ ] Add tests.
* [ ] Add temporary debug endpoint.

## Files

```text
backend/src/HarrisCountyAI.Application/Search/Retrieval/IRetrievalService.cs

backend/src/HarrisCountyAI.Application/Search/Retrieval/RetrievalRequest.cs

backend/src/HarrisCountyAI.Application/Search/Retrieval/RetrievedChunk.cs

backend/src/HarrisCountyAI.Infrastructure/Azure/Search/AzureRetrievalService.cs
```

---

# PR-24 — Reference Corpus Q&A

## Goal

Build the first complete RAG flow.

## Pipeline

```text
Question
↓
Retrieve evidence
↓
Build grounded prompt
↓
Azure LLM
↓
Answer
↓
Sources
```

## Checklist

* [ ] Create Q&A request.
* [ ] Create Q&A response.
* [ ] Create citation model.
* [ ] Define `IQuestionAnsweringService`.
* [ ] Build grounded system prompt.
* [ ] Add retrieved passages.
* [ ] Require evidence-based answers.
* [ ] Implement insufficient-evidence response.
* [ ] Return citations.
* [ ] Add API endpoint.
* [ ] Add tests with fake model.

## Files

```text
backend/src/HarrisCountyAI.Application/QuestionAnswering/IQuestionAnsweringService.cs

backend/src/HarrisCountyAI.Application/QuestionAnswering/QuestionAnsweringService.cs

backend/src/HarrisCountyAI.Application/QuestionAnswering/QuestionRequest.cs

backend/src/HarrisCountyAI.Application/QuestionAnswering/QuestionResponse.cs

backend/src/HarrisCountyAI.Application/QuestionAnswering/Citation.cs

backend/src/HarrisCountyAI.Application/QuestionAnswering/Prompts/GroundedQuestionPrompt.cs

backend/src/HarrisCountyAI.Api/Controllers/QuestionsController.cs
```

---

# PR-25 — Q&A Angular Interface

## Goal

Allow users to ask questions from the browser.

## Checklist

* [ ] Create Q&A component.
* [ ] Add question text box.
* [ ] Add submit action.
* [ ] Show loading state.
* [ ] Show answer.
* [ ] Show citations.
* [ ] Show insufficient-evidence result.
* [ ] Display source title.
* [ ] Display source page.
* [ ] Handle API errors.

## Files

```text
frontend/src/app/features/question-answering/

frontend/src/app/core/services/question-answering.service.ts

frontend/src/app/core/models/question-answer.model.ts
```

---

# PR-26 — Hybrid Search

## Goal

Upgrade vector-only retrieval to hybrid retrieval.

## Checklist

* [ ] Add keyword search.
* [ ] Combine vector and keyword retrieval.
* [ ] Configure Top K.
* [ ] Test exact section-number questions.
* [ ] Test form-number questions.
* [ ] Test semantic questions.
* [ ] Compare results against vector-only baseline.
* [ ] Add retrieval metrics logging.
* [ ] Update architecture documentation.

## Files

```text
backend/src/HarrisCountyAI.Infrastructure/Azure/Search/AzureRetrievalService.cs

backend/src/HarrisCountyAI.Application/Search/Retrieval/RetrievalRequest.cs

docs/architecture/rag-architecture.md

evaluation/datasets/retrieval/
```

---

# PR-27 — Semantic Reranking

## Goal

Use Azure's semantic ranking to improve candidate ordering.

## Pipeline

```text
Hybrid Search
↓
Top ~20
↓
Semantic ranking
↓
Best 3–5
```

## Checklist

* [ ] Configure semantic ranking.
* [ ] Define `IRerankingService`.
* [ ] Add Azure implementation where necessary.
* [ ] Retrieve larger initial candidate pool.
* [ ] Rerank candidates.
* [ ] Limit final context.
* [ ] Capture reranking score.
* [ ] Compare retrieval quality before/after.
* [ ] Add tests.

## Files

```text
backend/src/HarrisCountyAI.Application/Search/Reranking/IRerankingService.cs

backend/src/HarrisCountyAI.Infrastructure/Azure/Search/AzureSemanticRerankingService.cs

backend/src/HarrisCountyAI.Application/Search/Retrieval/RetrievedChunk.cs
```

---

# PR-28 — Case Document Indexing

## Goal

Make individual application documents searchable.

## Checklist

* [ ] Chunk case documents after extraction.
* [ ] Generate embeddings.
* [ ] Index case chunks.
* [ ] Add `SourceType = Case`.
* [ ] Add `CaseId`.
* [ ] Ensure `CaseId` is filterable.
* [ ] Update/reindex after document changes.
* [ ] Delete index records when documents are deleted.
* [ ] Add strict isolation tests.

## Files

```text
backend/src/HarrisCountyAI.Application/Documents/Indexing/CaseDocumentIndexingService.cs

backend/src/HarrisCountyAI.Infrastructure/Azure/Search/SearchIndexDefinition.cs

backend/tests/HarrisCountyAI.IntegrationTests/Search/CaseIsolationTests.cs
```

---

# PR-29 — Case-Specific Q&A

## Goal

Allow questions such as:

```text
Who signed this application?

What page contains the affidavit?

Did the applicant submit a drainage plan?
```

## Checklist

* [ ] Add retrieval scope option.
* [ ] Add Case-only scope.
* [ ] Require Case ID.
* [ ] Enforce CaseId search filter.
* [ ] Generate case-grounded answer.
* [ ] Return document/page citation.
* [ ] Add cross-case security tests.
* [ ] Update Angular Q&A screen.

## Files

```text
backend/src/HarrisCountyAI.Application/QuestionAnswering/QuestionScope.cs

backend/src/HarrisCountyAI.Application/QuestionAnswering/QuestionAnsweringService.cs

backend/src/HarrisCountyAI.Application/Search/Retrieval/RetrievalRequest.cs

frontend/src/app/features/question-answering/
```

---

# PR-30 — Dual-Source Retrieval

## Goal

Enable the project's most important comparison:

```text
What applicant submitted

vs.

What Harris County requires
```

## Checklist

* [ ] Add Case source retrieval.
* [ ] Add County source retrieval.
* [ ] Keep results separately labeled.
* [ ] Build comparison prompt.
* [ ] Add dual-source response model.
* [ ] Require citations from both sources when applicable.
* [ ] Add insufficient-evidence behavior.
* [ ] Add comparison tests.

## Files

```text
backend/src/HarrisCountyAI.Application/QuestionAnswering/DualSourceQuestionAnsweringService.cs

backend/src/HarrisCountyAI.Application/QuestionAnswering/Prompts/ComparisonPrompt.cs

backend/src/HarrisCountyAI.Application/Search/Retrieval/SourceType.cs

backend/tests/HarrisCountyAI.UnitTests/QuestionAnswering/
```

---

# PR-31 — Requirement Comparison Engine

## Goal

Move the submission-vs-requirement comparison into a repeatable service rather than only supporting it through chat.

## Checklist

* [ ] Define `Requirement`.
* [ ] Define `RequirementEvidence`.
* [ ] Define `SubmissionEvidence`.
* [ ] Define `RequirementComparisonResult`.
* [ ] Retrieve applicable county requirements.
* [ ] Inspect submitted documents.
* [ ] Match required documents.
* [ ] Reuse deterministic validation where possible.
* [ ] Use semantic evaluation only when needed.
* [ ] Produce final comparison list.
* [ ] Add tests.

## Files

```text
backend/src/HarrisCountyAI.Domain/Entities/Requirement.cs

backend/src/HarrisCountyAI.Application/Validation/Comparison/RequirementComparisonService.cs

backend/src/HarrisCountyAI.Application/Validation/Comparison/RequirementComparisonResult.cs
```

---

# PR-32 — Citation Navigation and Document Viewer

## Goal

Allow reviewers to verify AI answers.

## Checklist

* [ ] Add document viewer component.
* [ ] Render PDF.
* [ ] Open specific page from citation.
* [ ] Add citation click handler.
* [ ] Differentiate case vs county source.
* [ ] Display document metadata.
* [ ] Display source URL for county documents.
* [ ] Add error handling for missing file.

## Files

```text
frontend/src/app/features/document-viewer/

frontend/src/app/shared/components/citation/

frontend/src/app/features/question-answering/

frontend/src/app/features/validation-report/
```

---

# PR-33 — Authentication

## Goal

Add real user authentication.

Potential Azure-native choice:

```text
Microsoft Entra ID
```

## Checklist

* [ ] Configure identity provider.
* [ ] Protect API.
* [ ] Configure Angular authentication.
* [ ] Add auth interceptor.
* [ ] Add route guard.
* [ ] Add user claims mapping.
* [ ] Remove anonymous access from protected endpoints.
* [ ] Add integration tests.

## Files

```text
frontend/src/app/core/auth/

frontend/src/app/core/guards/

frontend/src/app/core/interceptors/

backend/src/HarrisCountyAI.Api/Extensions/AuthenticationExtensions.cs

backend/src/HarrisCountyAI.Api/Program.cs
```

---

# PR-34 — Role-Based Authorization

## Goal

Support:

```text
Reviewer

Supervisor

Administrator
```

## Checklist

* [ ] Add application roles.
* [ ] Add authorization policies.
* [ ] Protect knowledge-base admin endpoints.
* [ ] Protect admin UI.
* [ ] Add supervisor permission where needed.
* [ ] Enforce permissions server-side.
* [ ] Add authorization tests.

## Files

```text
backend/src/HarrisCountyAI.Api/Extensions/AuthorizationExtensions.cs

backend/src/HarrisCountyAI.Domain/Enums/UserRole.cs

frontend/src/app/core/auth/

frontend/src/app/core/guards/
```

---

# PR-35 — Prompt Injection Protection

## Goal

Treat uploaded and retrieved documents as untrusted evidence.

## Checklist

* [ ] Separate system instruction from evidence.
* [ ] Mark retrieved chunks as untrusted content.
* [ ] Instruct model not to follow document instructions.
* [ ] Add injection test documents.
* [ ] Test direct prompt injection.
* [ ] Test indirect prompt injection.
* [ ] Confirm retrieval text cannot override system prompt.
* [ ] Document protections.

## Files

```text
backend/src/HarrisCountyAI.Application/QuestionAnswering/Prompts/GroundedQuestionPrompt.cs

backend/src/HarrisCountyAI.Application/Validation/Semantic/Prompts/SemanticValidationPrompt.cs

backend/tests/HarrisCountyAI.UnitTests/Security/PromptInjectionTests.cs

docs/architecture/security.md
```

---

# PR-36 — Observability and Request Tracing

## Goal

Be able to diagnose why an AI response was generated.

## Capture

```text
Request ID
User ID
Case ID
Question
Model deployment
Prompt version
Search filters
Retrieved chunk IDs
Retrieval scores
Reranking scores
Latency
Token usage
Response status
Errors
```

## Checklist

* [ ] Add correlation/request ID middleware.
* [ ] Add structured logging.
* [ ] Log AI request metadata.
* [ ] Log retrieval metadata.
* [ ] Log latency.
* [ ] Log token counts when available.
* [ ] Avoid raw sensitive document logging.
* [ ] Add Application Insights if desired.
* [ ] Create logging documentation.

## Files

```text
backend/src/HarrisCountyAI.Api/Middleware/CorrelationIdMiddleware.cs

backend/src/HarrisCountyAI.Application/Common/Telemetry/

backend/src/HarrisCountyAI.Infrastructure/Telemetry/

backend/src/HarrisCountyAI.Api/Program.cs

docs/architecture/observability.md
```

---

# PR-37 — Retrieval Evaluation Dataset

## Goal

Measure whether search finds the correct evidence.

## Dataset Example

```json
{
  "question": "How long does the applicant have to respond?",
  "expectedDocument": "PermitPolicy.pdf",
  "expectedPage": 17
}
```

## Checklist

* [ ] Create evaluation dataset format.
* [ ] Add first 20–30 questions.
* [ ] Record expected documents.
* [ ] Record expected pages/sections.
* [ ] Create retrieval evaluation runner.
* [ ] Calculate Recall@1.
* [ ] Calculate Recall@3.
* [ ] Calculate Recall@5.
* [ ] Store baseline results.

## Files

```text
evaluation/datasets/retrieval/questions.json

evaluation/scripts/

docs/evaluation/evaluation-strategy.md
```

---

# PR-38 — Generation Evaluation

## Goal

Measure whether the LLM correctly answers once correct evidence is available.

## Checklist

* [ ] Create expected-answer dataset.
* [ ] Store expected facts.
* [ ] Run Q&A pipeline.
* [ ] Capture generated answer.
* [ ] Check citation presence.
* [ ] Identify unsupported claims.
* [ ] Store results.
* [ ] Establish baseline metrics.

## Files

```text
evaluation/datasets/generation/questions.json

evaluation/datasets/generation/results/

evaluation/scripts/
```

---

# PR-39 — LLM-as-a-Judge Evaluation

## Goal

Add an automated evaluator for development.

This judge should initially evaluate test runs, not every production response.

## Criteria

```text
Groundedness

Relevance

Completeness

Accuracy

Unsupported Claims
```

## Checklist

* [ ] Define evaluation result schema.
* [ ] Create judge prompt.
* [ ] Add judge service.
* [ ] Require structured response.
* [ ] Run against generation dataset.
* [ ] Save evaluation results.
* [ ] Compare against manually reviewed examples.

## Files

```text
backend/src/HarrisCountyAI.Application/Evaluation/

backend/src/HarrisCountyAI.Application/Evaluation/Prompts/JudgePrompt.cs

evaluation/datasets/generation/results/
```

---

# PR-40 — Failure Handling and Resilience

## Goal

Make Azure dependencies safe enough for a realistic enterprise application.

## Checklist

* [ ] Add standardized API error responses.
* [ ] Add global exception middleware.
* [ ] Add Azure timeout handling.
* [ ] Add retry policies where appropriate.
* [ ] Handle search unavailable.
* [ ] Handle Document Intelligence unavailable.
* [ ] Handle model endpoint unavailable.
* [ ] Handle malformed model output.
* [ ] Handle Blob Storage failures.
* [ ] Display useful frontend errors.
* [ ] Add failure-path tests.

## Files

```text
backend/src/HarrisCountyAI.Api/Middleware/ExceptionHandlingMiddleware.cs

backend/src/HarrisCountyAI.Application/Common/Exceptions/

backend/src/HarrisCountyAI.Infrastructure/DependencyInjection.cs

frontend/src/app/core/interceptors/error.interceptor.ts
```

---

# PR-41 — Infrastructure as Code

## Goal

Make Azure infrastructure reproducible.

Bicep is a logical choice given the Azure-first architecture.

## Checklist

* [ ] Define resource group structure.
* [ ] Define Storage Account.
* [ ] Define Blob containers.
* [ ] Define Azure AI Search.
* [ ] Define Document Intelligence resource.
* [ ] Define Azure SQL.
* [ ] Define backend hosting.
* [ ] Define frontend hosting.
* [ ] Define Application Insights.
* [ ] Parameterize environment names.
* [ ] Document secrets that are intentionally not committed.

## Files

```text
infrastructure/bicep/main.bicep

infrastructure/bicep/storage.bicep

infrastructure/bicep/search.bicep

infrastructure/bicep/database.bicep

infrastructure/bicep/app-service.bicep

infrastructure/README.md
```

---

# PR-42 — CI Pipeline

## Goal

Require the project to build and test on every pull request.

## Checklist

* [ ] Create GitHub Actions workflow.
* [ ] Restore backend dependencies.
* [ ] Build backend.
* [ ] Run .NET tests.
* [ ] Install frontend dependencies.
* [ ] Build Angular.
* [ ] Run Angular tests.
* [ ] Add formatting/lint check.
* [ ] Fail PR when tests fail.

## Files

```text
.github/workflows/ci.yml
```

---

# PR-43 — Deployment Pipeline

## Goal

Deploy the application from GitHub.

## Checklist

* [ ] Create development Azure environment.
* [ ] Configure GitHub environment.
* [ ] Add GitHub secrets / federated identity.
* [ ] Deploy backend.
* [ ] Deploy frontend.
* [ ] Run database migrations.
* [ ] Configure backend environment variables.
* [ ] Configure Azure AI resources.
* [ ] Add deployment smoke test.

## Files

```text
.github/workflows/deploy-dev.yml

infrastructure/

README.md
```

---

# PR-44 — MVP End-to-End Testing

## Goal

Verify the complete workflow.

## Test Scenario

```text
Create case
↓
Upload application documents
↓
Store in Blob
↓
Extract
↓
Normalize
↓
Validate
↓
Index
↓
View validation report
↓
Ask case question
↓
Ask county question
↓
Compare submission against requirement
↓
Verify citations
```

## Checklist

* [ ] Test valid application.
* [ ] Test incomplete application.
* [ ] Test malformed document.
* [ ] Test semantic validation.
* [ ] Test county question.
* [ ] Test case question.
* [ ] Test dual-source question.
* [ ] Test citation navigation.
* [ ] Test insufficient evidence.
* [ ] Test prompt injection.
* [ ] Test case isolation.
* [ ] Document known limitations.

## Files

```text
backend/tests/HarrisCountyAI.IntegrationTests/EndToEnd/

frontend/tests/

docs/testing/mvp-test-plan.md
```

---

# PR-45 — MVP Polish and Documentation

## Goal

Prepare the project to be demonstrated and discussed in an interview.

## Checklist

* [ ] Update root README.
* [ ] Add architecture diagram.
* [ ] Document setup instructions.
* [ ] Document required Azure services.
* [ ] Document environment variables.
* [ ] Document selected workflow.
* [ ] Document deterministic validation design.
* [ ] Document semantic validation design.
* [ ] Document RAG pipeline.
* [ ] Document security decisions.
* [ ] Document evaluation methodology.
* [ ] Document known limitations.
* [ ] Add screenshots.
* [ ] Add sample questions.
* [ ] Add demo walkthrough.

## Files

```text
README.md

docs/architecture/system-overview.md

docs/architecture/rag-architecture.md

docs/architecture/security.md

docs/evaluation/evaluation-strategy.md

docs/demo/demo-script.md
```

---

# 3. Recommended PR Milestones

Although there are 45 proposed PRs, they naturally fall into larger milestones.

## Milestone 1 — Basic Web Application

```text
PR-01 → PR-08
```

At this point:

```text
Angular
↓
ASP.NET Core
↓
SQL
↓
Blob Storage
```

works.

Users can create cases and upload documents.

---

# Milestone 2 — Document Review

```text
PR-09 → PR-14
```

At this point:

```text
Upload
↓
Extract
↓
Normalize
↓
Validate
↓
Validation Report
```

works without needing an LLM.

This is an important milestone because it proves the core application architecture before adding AI.

---

# Milestone 3 — Semantic AI

```text
PR-15 → PR-16
```

At this point the application can handle questions such as:

```text
Does this response meaningfully satisfy the requirement?
```

---

# Milestone 4 — Harris County Knowledge Base

```text
PR-17 → PR-22
```

At this point:

```text
County PDF
↓
Extract
↓
Chunk
↓
Embed
↓
Index
```

works.

---

# Milestone 5 — RAG

```text
PR-23 → PR-27
```

At this point the application can answer:

```text
What does Harris County require?
```

using grounded sources.

---

# Milestone 6 — Application-Aware RAG

```text
PR-28 → PR-32
```

This is the project's major differentiating milestone.

The system can answer:

```text
What did the applicant submit?
```

and:

```text
What was the applicant required to submit?
```

and then compare the two.

---

# Milestone 7 — Enterprise Security

```text
PR-33 → PR-36
```

At this point the project demonstrates:

```text
Authentication
Authorization
Case isolation
Prompt injection protection
Observability
```

---

# Milestone 8 — AI Evaluation

```text
PR-37 → PR-39
```

At this point you can actually measure whether changes to:

```text
chunking
retrieval
reranking
prompts
models
```

improve the application rather than guessing.

---

# Milestone 9 — Production Hardening

```text
PR-40 → PR-45
```

This covers:

```text
resilience
infrastructure
CI/CD
deployment
E2E testing
documentation
```

---

# 4. What I Would Build First

If the goal is both learning and producing something you can discuss during interviews, I would focus heavily on completing:

```text
PR-01
through
PR-32
```

before spending significant time on deployment polish.

The most important technical story is:

```text
Angular
↓
ASP.NET Core
↓
Document Upload
↓
Azure Blob Storage
↓
Document Intelligence
↓
Normalized Domain Model
↓
C# Validation
↓
Selective LLM Validation
↓
Azure AI Search
↓
Hybrid Retrieval
↓
Semantic Reranking
↓
Azure-hosted LLM
↓
Grounded Answer
↓
Citations
```

That architecture directly demonstrates the main engineering philosophy of the project: deterministic rules remain in C#, while retrieval and LLM reasoning are used where semantic understanding is actually necessary.

---

# 5. Important Development Rule

Do not let later PRs leak heavily into earlier PRs.

For example:

### PR-03

Should establish persistence.

Do not also add:

```text
RAG
LLMs
Azure AI Search
Agents
```

because they will eventually be needed.

### PR-11

Should build deterministic validation.

Do not add LLM logic inside `RequiredFieldRule`.

### PR-15

Should introduce the model abstraction.

Do not rewrite the application around the Azure SDK.

### PR-23

Should introduce retrieval.

Do not simultaneously add agents.

The value of this PR structure is that every GitHub merge tells a clear architectural story.

---

# 6. Suggested GitHub Progress View

Create GitHub milestones such as:

```text
M1 — Application Foundation

M2 — Document Processing

M3 — AI Validation

M4 — Knowledge Corpus

M5 — RAG

M6 — Dual-Source Review

M7 — Security

M8 — Evaluation

M9 — Production Readiness
```

Then assign each PR/issue to the appropriate milestone.

That will give you a repository history that reads almost like a technical case study of how the application was built.

---

# 7. MVP Stop Line

The MVP should stop after we can reliably demonstrate:

* [ ] Reviewer creates a case.
* [ ] Reviewer uploads multiple PDFs.
* [ ] Documents are stored securely.
* [ ] Azure Document Intelligence extracts them.
* [ ] Extracted output is normalized.
* [ ] C# rules identify deterministic problems.
* [ ] LLM identifies selected semantic problems.
* [ ] Validation report is displayed.
* [ ] County corpus can be managed.
* [ ] County documents are chunked and embedded.
* [ ] Azure AI Search indexes the corpus.
* [ ] Hybrid retrieval works.
* [ ] Semantic reranking works.
* [ ] Reviewer can ask questions about county requirements.
* [ ] Reviewer can ask questions about the active case.
* [ ] Case retrieval is isolated by `CaseId`.
* [ ] System can compare county requirements against submitted evidence.
* [ ] AI answers provide citations.
* [ ] Reviewer can open cited source material.
* [ ] System abstains when evidence is insufficient.
* [ ] Retrieval quality can be measured.
* [ ] Generation quality can be evaluated.
* [ ] Critical application activity can be traced.

Anything beyond that—agents, critic loops, automatic county actions, automated approvals, additional permit types, fine-tuning, or multi-agent orchestration—should stay outside the first MVP.

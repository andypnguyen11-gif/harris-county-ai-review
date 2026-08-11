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

## Status

Planning phase. See [`PRD.md`](PRD.md) for the product requirements and [`Tasks.md`](Tasks.md) for the PR-by-PR implementation plan.

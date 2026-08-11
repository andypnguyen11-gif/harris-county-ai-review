# API Endpoints

Base URL (local development): `https://localhost:7147` / `http://localhost:5147` (see `backend/src/HarrisCountyAI.Api/Properties/launchSettings.json`).

All enums are serialized as strings. Timestamps are UTC in ISO-8601 format. Validation errors return `400` with an RFC 7807 `application/problem+json` body.

## Health

### GET /health

Returns `200 OK` with body `Healthy` when the API is running.

## Cases

### Case resource

```json
{
  "id": "8f7f9d3e-2f2a-4f0e-9c93-1c9d3c5b6a01",
  "caseNumber": "HC-2026-0001",
  "name": "Creek Bend Development",
  "workflowType": "FloodplainDevelopmentPermit",
  "status": "New",
  "createdAt": "2026-08-11T19:04:32.1234567Z",
  "updatedAt": "2026-08-11T19:04:32.1234567Z"
}
```

- `workflowType`: `FloodplainDevelopmentPermit`
- `status`: `New`, `Processing`, `ReadyForReview`, `InReview`, `Completed`
- `caseNumber` is server-generated per year: `HC-{year}-{sequence:0000}` (e.g. `HC-2026-0001`).

### POST /api/cases

Creates a case. Status starts as `New`.

Request body:

```json
{
  "name": "Creek Bend Development",
  "workflowType": "FloodplainDevelopmentPermit"
}
```

Responses:

- `201 Created` — the created case resource; `Location` header points at `GET /api/cases/{id}`.
- `400 Bad Request` — `name` missing/blank, or `workflowType` not a known value.

### GET /api/cases

Returns `200 OK` with an array of case resources, newest first.

### GET /api/cases/{id}

Responses:

- `200 OK` — the case resource.
- `404 Not Found` — no case with that id.

### PATCH /api/cases/{id}

Partial update; omitted fields are left unchanged.

Request body (both fields optional):

```json
{
  "name": "Creek Bend Development (Revised)",
  "status": "InReview"
}
```

Responses:

- `200 OK` — the updated case resource.
- `400 Bad Request` — `name` present but blank, or `status` not a known value.
- `404 Not Found` — no case with that id.

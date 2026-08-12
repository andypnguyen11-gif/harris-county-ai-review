# Frontend

The Angular reviewer UI for the Harris County AI Document Review Assistant. Generated with
[Angular CLI](https://github.com/angular/angular-cli) 22.1.3. Start at the
[root README](../README.md) for what the application does and how the pieces fit together.

## Layout

| Path | Contents |
|---|---|
| `src/app/features/` | One folder per screen: `sign-in`, `dashboard`, `cases/*`, `document-upload`, `validation-report`, `document-viewer`, `question-answering`, `knowledge-base` |
| `src/app/core/` | Cross-cutting code: `auth/`, `guards/`, `interceptors/`, `services/`, `models/`, `errors/` |
| `src/app/shared/` | Reusable presentational components (`status-badge`, `citation`, `error-message`) |
| `src/environments/` | `apiUrl`, the only environment-dependent value; rewritten by the deploy workflow |

Routes are declared in `src/app/app.routes.ts` and every one is lazy-loaded. `/knowledge-base` is
additionally gated on the `Administrator` role.

## Commands

```bash
npm ci
npm start                    # ng serve on http://localhost:4200
npm run build                # production build into dist/frontend
npx ng test --watch=false    # Vitest, single run — this is what CI runs
```

## Talking to the API

`src/environments/environment.ts` points at `http://localhost:5096/api`, which is the backend's
`http` launch profile. The call is cross-origin and the backend allows it: running in the Development
environment, the API registers a `LocalDevelopment` CORS policy admitting the origins listed under
`Cors:AllowedOrigins` in `appsettings.Development.json`, which ships with `http://localhost:4200`.
Serve this app on another port and that origin has to be added there too. There is no dev-server
proxy — `apiUrl` stays absolute. The test suite is unaffected either way; it uses
`HttpTestingController` rather than a real server.

Authentication in local development is the backend's dev-token endpoint: the sign-in screen posts a
username, and `AuthService` stores the returned session in `sessionStorage`. `authInterceptor`
attaches the bearer token to API calls and signs out on a `401`.

## Tests

Vitest with jsdom, run through the Angular CLI's `@angular/build:unit-test` builder — there is no
Karma or Jasmine. `src/app/reviewer-workflow.spec.ts` walks the whole reviewer journey (sign in →
create case → upload → process → validate → open a cited document → ask a question in both scopes)
against a mocked HTTP backend.

There is **no browser end-to-end harness** — no Playwright, Cypress, or `ng e2e` configuration. The
journey is covered at the HTTP-contract level on the frontend and end to end on the backend, but
nothing drives a real browser.

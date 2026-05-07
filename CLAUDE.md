# Pacevite

Fitness event tracking app. Athletes upload race results (CSV/JSON), view personal bests, and chat with an AI coach.

## Stack

| Layer | Tech |
|---|---|
| API | .NET 10, ASP.NET Core (Slim), EF Core 10, PostgreSQL 17 |
| Auth | ASP.NET Identity + JWT Bearer |
| Mediator | [Mediator](https://github.com/martinothamar/Mediator) (source-gen) + FluentValidation pipeline |
| Frontend | React 19, Vite, TypeScript, TanStack Query v5, React Router v7, Tailwind v4 |
| AI | Anthropic SDK (`claude-haiku-4-5-20251001`) via `IChatToolHandler` pattern |

## Port Map

| Service | URL |
|---|---|
| API (dev) | `http://localhost:5291` |
| Frontend (dev) | `http://localhost:5173` |
| Nginx proxy | `http://localhost:8080` → `/apis/pacevite/` → API |
| PostgreSQL | `localhost:5432` |

## Dev Setup

**Prerequisite:** Podman installed (project uses `podman`, not `docker`).

```bash
# 1. Start the database
podman compose up -d db

# 2. Start the API (auto-migrates DB on first run)
dotnet run --project src/Pacevite.Api --launch-profile http

# 3. Start the frontend
cd src/Pacevite.Web && npm run dev
```

Credentials for the dev DB are in `appsettings.Development.json` (committed — dev only).

## Environment

Required environment variables in production (dev uses `appsettings.Development.json`):

| Variable | Purpose |
|---|---|
| `ANTHROPIC_API_KEY` | Claude API key for AI coaching endpoint |
| `DB_USER` / `DB_PASSWORD` | PostgreSQL credentials |
| `JWT_SECRET` | HMAC signing key (min 32 chars) |
| `JWT_ISSUER` / `JWT_AUDIENCE` | JWT validation values |

Dev credentials are committed in `appsettings.Development.json` — never use in production.

## Running Tests

```bash
# .NET unit + integration tests (Testcontainers spins up real Postgres)
# NOTE: TUnit uses Microsoft.Testing.Platform — dotnet test is NOT supported on .NET 10; use dotnet run
dotnet run --project tests/Pacevite.Api.Tests

# Filter by category (.NET only)
dotnet run --project tests/Pacevite.Api.Tests -- --treenode-filter "/*/*/*/*[Category=Unit]"
dotnet run --project tests/Pacevite.Api.Tests -- --treenode-filter "/*/*/*/*[Category=Integration]"

# Frontend unit tests (Vitest + Testing Library + MSW)
cd src/Pacevite.Web && npm test

# E2E tests (Playwright — auto-starts API + frontend if not running)
cd src/Pacevite.Web && npm run test:e2e
```

## Architecture

```
src/
  Pacevite.Api/
    Features/         # Vertical slices — one folder per feature
      Auth/           # Register + Login endpoints
      Events/         # Upload, GetEvents, GetEventById, GetPersonalBests, DeleteEvent
    Infrastructure/
      Auth/           # JwtTokenService
      Chat/           # IChatToolHandler + ChatToolExecutor (Anthropic AI)
      OpenApi/        # ForwardedPrefixTransformer for Scalar UI
      Parsing/        # IEventParser (CSV + JSON implementations)
      Persistence/    # AppDbContext (EF Core + Identity)
    Contracts/        # Request/response DTOs — Requests/ and Responses/
    Pipeline/         # ValidationBehavior (Mediator pipeline)
    Domain/           # Entities, Enums
    Migrations/       # EF Core migrations

  Pacevite.Web/
    src/
      pages/          # LoginPage, RegisterPage, DashboardPage, UploadPage, EventDetailPage
      components/     # AuthGuard, ThemeToggle, chart components (ProgressChart, PbPanel, SplitChart, RaceComparison)
      context/        # AuthContext (JWT token in memory)
      hooks/          # useAuth, useEvents, useEvent
      lib/            # api.ts (fetch client), types.ts (shared TS types), chartUtils.ts
      test/           # render.tsx (renderWithProviders), handlers.ts (MSW), setup.ts

tests/
  Pacevite.Api.Tests/
    Unit/             # Unit tests — Parsers/, Chat/, Prediction/
    Integration/      # Auth + Event endpoint integration tests (WebApplicationFactory)
```

Each feature slice owns its own command/query, handler, validator, and endpoint registration. **Do not add handler logic to Infrastructure or Pipeline layers.**

## Key Patterns

### Adding a new feature
Use the `/add-feature` skill — scaffolds the vertical slice (command, handler, validator, endpoint registration) and wires it into `Program.cs`.

### Adding an event parser
Use the `/add-parser` skill — scaffolds `IEventParser` + unit tests + `Program.cs` registration.

### Adding an AI chat tool
Use the `/add-chat-tool` skill — scaffolds `IChatToolHandler` + unit tests + DI registration.

### EF Core migrations
Use the `/ef-migrate` skill — runs the 3-step migration workflow.

### Seeding the dev database
Use the `/seed-db` skill — registers/logs in via the API and uploads `sample-data/my-events.json`.

### Deciding which test layer to use
Use the `/gen-test` skill — routing table for Unit vs Integration vs E2E.

## Frontend Test Utilities

All component tests use `renderWithProviders` from `src/test/render.tsx`:
```ts
renderWithProviders(<MyComponent />, { authenticated: true, initialEntries: ['/path'] })
```
It wraps with `QueryClientProvider` + `MemoryRouter` + a mocked `AuthContext`. MSW request handlers live in `src/test/handlers.ts`.

## Gotchas

- **`podman`, not `docker`** — `docker compose` will fail; use `podman compose`.
- **Auto-migration** — `Database.Migrate()` runs at startup in `Development`. No manual `dotnet ef database update` step needed locally.
- **Rate limiting** — Auth endpoints are capped at 10 req/min in production. `appsettings.Development.json` overrides this to 1000 so tests don't 429.
- **Multiple `IEventParser` registrations** — all parsers are registered as `IEventParser` singletons; the upload endpoint iterates them and calls `CanParse(contentType)` to dispatch. Add new parsers in `Program.cs` alongside the existing registrations.
- **Nginx base path** — the proxy strips `/apis/pacevite/` and forwards `X-Forwarded-Prefix` so Scalar's OpenAPI URLs render correctly. The `ForwardedPrefixTransformer` in `Infrastructure/OpenApi/` handles this.
- **`EventType` and `Completion` must be uppercased** — normalise in the parser, not the handler.
- **Mediator is source-generated** — adding a new `ICommand`/`IQuery` handler requires a build before the handler is discoverable.

## Specs

- Specs live in `docs/specs/`
- Read `docs/specs/00-overview.md` before starting any work on this project

## Documentation

- Read `docs/specs/00-overview.md` before starting any work
- Read `docs/specs/01-architecture.md` before making architectural changes
- Read all files in `docs/decisions/` before proposing architectural decisions
- Add new ADRs to `docs/decisions/` using MADR format when making significant decisions

## ADR Workflow

- ADRs live in `docs/decisions/`
- Before any architectural decision, read all existing files in `docs/decisions/`
- To create a new ADR, find the highest numbered file in `docs/decisions/`, increment by 1, and create a new file named `NNNN-short-title-in-kebab-case.md`
- Use MADR format for every ADR:
  - **Title** — short imperative phrase
  - **Status** — Proposed | Accepted | Deprecated | Superseded by [NNNN]
  - **Context and Problem Statement** — why this decision is needed
  - **Considered Options** — all options evaluated
  - **Decision Outcome** — what was chosen and why
  - **Consequences** — what becomes easier or harder
- If superseding an existing ADR, update its status line to `Superseded by [new NNNN]` before creating the replacement

## PR Review

### Severity Definitions

| Level | When to use |
|-------|-------------|
| 🔴 **Important** | Incorrect logic, auth bypass, data exposure, breaking changes, unhandled exceptions |
| 🟡 **Nit** | Style, naming, minor refactors — at most 5 per review |
| 💡 **Suggestion** | Non-blocking improvement worth considering but not required |

If all findings are nits, open the summary with: _"No blocking issues."_

### Always Flag as 🔴 Important

#### Logic & Reliability
- Incorrect business logic that would break production behavior
- Unhandled `Task` exceptions — `async void` outside event handlers, fire-and-forget without `.ContinueWith` or a surrounding `try/catch`
- `.Result` or `.Wait()` called on a `Task` (deadlock risk in ASP.NET context)
- `catch (Exception)` blocks that swallow exceptions without logging or rethrowing

#### Auth & Security
- Missing `[Authorize]` on controller actions or endpoints that access user/tenant data
- Authorization checks missing after authentication — validate claims, not just identity
- SQL built via string interpolation or concatenation (use parameterized queries / EF Core)
- Hardcoded secrets, API keys, connection strings, or credentials anywhere in the diff
- Sensitive fields (SSN, DOB, passwords, tokens) returned in API responses without masking
- User IDs, emails, or PII appearing in log statements or exception messages

#### Data & Migrations
- Backward-incompatible database migrations (column drops, type changes without fallback)
- EF Core N+1 queries — `.ToList()` called inside a loop, or missing `.Include()` on related entities

#### API Contracts
- HTTP status codes mismatched to semantics (e.g., `200 OK` with an error body)
- Endpoints that return `null` where a proper `404 NotFound` should be used
- New public endpoints missing request validation (FluentValidation or DataAnnotations)

### C# / .NET Specific Checks

```
async/await
- Flag: async void (outside event handlers)
- Flag: .Result or .Wait() on Tasks
- Flag: missing CancellationToken propagation through async call chains
- Flag: missing ConfigureAwait(false) in library/infrastructure code

HttpClient
- Flag: new HttpClient() — must use IHttpClientFactory

Nullable reference types
- Flag: ! null-forgiving operator without an explanatory comment

EF Core
- Flag: N+1 patterns (ToList inside a loop, missing Include)
- Flag: raw SQL via string interpolation

DI / Configuration
- Flag: hardcoded base URLs — use IOptions<T> or IConfiguration
- Flag: service locator pattern (IServiceProvider.GetService inside business logic)

Logging
- Flag: sensitive data (emails, IDs, tokens) in log messages
- Flag: missing structured logging — prefer Log.Information("{Key} {Value}", ...) over string concat
```

### API & Service Boundary Rules

- New endpoints must have at minimum one integration test
- All controller inputs must be validated before use
- Outbound HTTP calls must go through registered `IHttpClientFactory` clients — no raw `new HttpClient()`
- Cancellation tokens must be threaded through async call chains, not dropped at the controller
- Inter-service communication must not hardcode URLs — externalize to config/environment

### What NOT to Review

- `*Migrations/*.cs` — EF Core migration files (structure, not behavior)
- `*.Designer.cs`, `*.g.cs`, `**/obj/**`, `**/bin/**`
- `packages.lock.json`, `*.lock`, `**/node_modules/**`
- Formatting violations — enforced by `dotnet format` in CI
- Build warnings and compiler errors — surfaced by CI before review

Do not flag issues in `**Tests/**` files for production-only patterns (e.g., DI registration style, configuration access).

### Nit Volume Cap

Post at most **5 🟡 Nit** comments per review. If more were found: append _"plus N similar style items not individually listed"_ to the summary.

### Re-Review Behavior

On subsequent pushes after the first full review:

- Suppress nits already raised or stylistically similar to prior feedback
- Only post 🔴 Important findings or issues **newly introduced** since the last reviewed commit
- Do not re-litigate resolved threads

### Evidence Requirement

Before posting any finding:

1. Cite the exact `file:line` reference
2. Read the implementation — do not infer behavior from method or variable naming alone
3. If the bug cannot be confirmed from the diff and surrounding context, **do not post it**

False positives erode trust. When uncertain, use 💡 Suggestion instead.

### Summary Format

End every review with:

```
## Review Summary
- 🔴 Important: <count> issue(s)
- 🟡 Nit: <count> (capped at 5)
- 💡 Suggestions: <count>

Overall: <one sentence verdict>
```

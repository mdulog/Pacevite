# Pacevite — Project Overview

## Project Overview

Pacevite is a fitness event tracking web application for endurance and functional-fitness athletes. Athletes upload race results in CSV or JSON format, review their event history, inspect personal bests, visualise split performance against their own averages, and (in a future state) receive AI-coached feedback from an Anthropic model.

The project is a mixed monorepo: an ASP.NET Core Slim API on .NET 10 sits beside a React 19 / TypeScript / Vite frontend. Both are containerised and served in production behind an Nginx reverse proxy.

## Purpose

| Actor | Need |
|---|---|
| Athlete | Upload race results without manual data entry |
| Athlete | Track finish-time progress per event type over time |
| Athlete | Identify personal bests across event types (Finished completions only) |
| Athlete | Understand split-level performance relative to personal average |
| Athlete (future) | Receive AI coaching insights via a chat interface |

## Core Use Cases

1. Register a new account with an email address and password.
2. Log in to receive a short-lived JWT.
3. Upload a CSV or JSON file containing one or more race results.
4. View the full event history with client-side search and pagination.
5. View personal bests (fastest `Finished` result per event type).
6. Drill into a single event to see split breakdown and comparison to personal averages.
7. Delete an event from the history.
8. Toggle between light and dark UI themes.

## Technology Stack

| Layer | Technology | Version |
|---|---|---|
| API runtime | .NET / ASP.NET Core Slim (`WebApplication.CreateSlimBuilder`) | .NET 10 |
| ORM | EF Core | 10 |
| Database | PostgreSQL | 17 |
| Auth | ASP.NET Core Identity + JWT Bearer | bundled with .NET 10 |
| Mediator | martinothamar/Mediator (source-generated) | — |
| Validation | FluentValidation | — |
| OpenAPI UI | Scalar | — |
| Frontend runtime | Node / Vite | — |
| UI framework | React | 19 |
| Language | TypeScript | — |
| Routing | React Router | v7 |
| Server state | TanStack Query | v5 |
| Styling | Tailwind CSS | v4 |
| Charts | Recharts | — |
| HTTP client | Axios | — |
| Test runner (.NET) | TUnit + Testcontainers | — |
| Test runner (frontend) | Vitest + React Testing Library | — |
| Mock server | MSW | v2 |
| E2E | Playwright | — |
| Container runtime | Podman (dev) / Docker-compatible compose | — |
| Reverse proxy | Nginx | alpine |
| AI SDK | Anthropic SDK | — |

## High-Level Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                        Client Browser                       │
│                React 19 SPA (Vite / TypeScript)             │
│          TanStack Query │ React Router v7 │ Recharts         │
└──────────────────────────────┬──────────────────────────────┘
                               │ /api/*  (dev: Vite proxy)
                               │ /apis/pacevite/* (prod: Nginx)
┌──────────────────────────────▼──────────────────────────────┐
│                       Nginx Reverse Proxy                   │
│      Strips /apis/pacevite/ prefix, forwards X-Forwarded-*  │
└──────────────────────────────┬──────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────┐
│                    ASP.NET Core Slim API                    │
│  ┌──────────┐  ┌─────────────┐  ┌──────────────────────┐   │
│  │ Features │  │  Pipeline   │  │   Infrastructure     │   │
│  │ (slices) │→ │ Validation  │→ │ Auth / Parsing /     │   │
│  │          │  │ Behavior    │  │ Persistence / Chat / │   │
│  └──────────┘  └─────────────┘  │ OpenApi              │   │
│                                 └──────────────────────┘   │
└──────────────────────────────┬──────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────┐
│                      PostgreSQL 17                          │
│           Events │ EventSplits │ AspNetUsers (Identity)     │
└─────────────────────────────────────────────────────────────┘
```

## Major Components

### HTTP API

| Route group | Purpose |
|---|---|
| `POST /api/auth/register` | Create account, return JWT |
| `POST /api/auth/login` | Authenticate, return JWT |
| `POST /api/events/upload` | Parse and persist uploaded CSV/JSON file |
| `GET /api/events` | List events (filterable by type and date range) |
| `GET /api/events/personal-bests` | Fastest Finished result per event type |
| `GET /api/events/{id}` | Single event with ordered splits |
| `DELETE /api/events/{id}` | Remove an event (always returns 204) |
| `GET /health` | Liveness/readiness health check (JSON) |

### Non-HTTP Services

| Component | Type | Status |
|---|---|---|
| `ValidationBehavior<TMessage, TResponse>` | Mediator pipeline behavior — runs before every command/query handler | Registered and active |
| `ValidationExceptionHandler` | `IExceptionHandler` — converts `ValidationException` to RFC 7807 `400` | Registered and active |
| `JwtTokenService` | Scoped service generating HMAC-SHA256 JWTs | Registered and active |
| `CsvEventParser` | Singleton `IEventParser` — parses `text/csv` uploads | Registered and active |
| `JsonEventParser` | Singleton `IEventParser` — parses `application/json` uploads | Registered and active |
| `IChatToolHandler` / `ChatToolExecutor` | Plugin pattern for Anthropic AI tool calls | Scaffolded — **not registered in DI** |
| EF Core auto-migration | Runs `Database.Migrate()` at startup in `Development` | Development only |
| Rate limiter (`auth` fixed-window) | ASP.NET Core rate limiting middleware (10 req/min default) | Active |

### Frontend Feature Areas

| Area | Pages / Components |
|---|---|
| Authentication | `LoginPage`, `RegisterPage`, `AuthContext`, `AuthGuard` |
| Event dashboard | `DashboardPage` — event list with search, client-side pagination, delete |
| Progress analytics | `ProgressChart` (finish-time trend line), `PbPanel` (type-selector + PB bar) |
| Personal bests | `DashboardPage` PB grid cards (sourced from `/events/personal-bests`) |
| Event detail | `EventDetailPage` — split breakdown (`SplitChart`), race comparison (`RaceComparison`) |
| File upload | `UploadPage` — drag-zone, format validation, mutation + cache invalidation |
| Theme | `ThemeContext`, `ThemeToggle` — light/dark persisted to `localStorage` |

### Shared UI Libraries

| Module | Purpose |
|---|---|
| `lib/api.ts` | Axios client, `tokenStore` (in-memory JWT), request interceptor |
| `lib/types.ts` | Shared TypeScript interfaces mirroring API contracts + `formatTime` |
| `lib/chartUtils.ts` | `groupByEventType`, `computePbs`, `computeAverageSplits`, `computeSplitDeltas`, `formatElapsed` |
| `test/render.tsx` | `renderWithProviders` — wraps components with `QueryClientProvider`, `MemoryRouter`, mocked `AuthContext` |
| `test/handlers.ts` | MSW v2 request handlers for unit tests |

## External Integrations

| Integration | Purpose | Config key |
|---|---|---|
| PostgreSQL 17 | Primary data store | `ConnectionStrings__Default` |
| ASP.NET Identity | User management (password hashing, `UserManager`) | built-in |
| Anthropic API | AI coaching (scaffolded, not active) | `Anthropic__ApiKey`, `Anthropic__Model` |
| Nginx | Reverse proxy in production | `./nginx/prod.conf` (path assumed) |

## Configuration

### Required Environment Variables (Production)

| Variable | Purpose |
|---|---|
| `DB_USER` / `DB_PASSWORD` | PostgreSQL credentials |
| `JWT_SECRET` | HMAC signing key (minimum 32 characters) |
| `JWT_ISSUER` | JWT `iss` claim value |
| `JWT_AUDIENCE` | JWT `aud` claim value |
| `ANTHROPIC_API_KEY` | Anthropic API key (required by container env; chat endpoints not yet wired) |
| `ANTHROPIC_MODEL` | Anthropic model identifier (defaults to `claude-sonnet-4-6` in `docker-compose.yml`) |

### Development Overrides

Dev credentials live in `appsettings.Development.json` (committed — dev only). The rate limiter defaults to 1000 req/min in development via `RateLimit:Auth:PermitLimit`.

## Deployment

### Development

```bash
# 1. Start the database
podman compose up -d db

# 2. Start the API (auto-migrates on first run)
dotnet run --project src/Pacevite.Api --launch-profile http

# 3. Start the frontend (Vite dev server with /api proxy to :5291)
cd src/Pacevite.Web && npm run dev
```

### Production (Compose)

`docker-compose.yml` defines four services: `proxy` (Nginx), `api` (ASP.NET Core), `web` (Nginx serving the Vite build), and `db` (PostgreSQL 17). The `api` service health-checks `GET /health` before `proxy` starts routing.

## Port Map

| Service | URL |
|---|---|
| API (dev) | `http://localhost:5291` |
| Frontend (dev) | `http://localhost:5173` |
| Nginx proxy (prod) | `http://localhost:8080` |
| PostgreSQL | `localhost:5432` |

## Known Limitations and In-Progress Features

| Area | Status / Gap |
|---|---|
| AI coaching | `IChatToolHandler` / `ChatToolExecutor` infrastructure is scaffolded but no implementations are registered in `Program.cs`. No chat endpoint exists. |
| Performance prediction | `LinearRegressionTests.cs` references `Pacevite.Api.Infrastructure.Regression.LinearRegression` which does not exist — the test project does not compile. |
| `GetEventsQuery` validation | No `FluentValidation` validator exists for `GetEventsQuery`. An invalid `eventType` query string is silently ignored and returns unfiltered results. |
| MSW mock contract drift | The upload MSW handler returns `eventType: 'HALF_MARATHON'`, which is not a valid `EventType` enum value on the backend (`Marathon`, `Hyrox`, `Spartan`, `Generic`). |
| JWT expiry not configurable | `JwtTokenService` uses a hardcoded `ExpiryMinutes = 60` constant. There is no configuration key to override this per environment. |
| Anthropic model ambiguity | `CLAUDE.md` names `claude-haiku-4-5-20251001`; `docker-compose.yml` defaults `ANTHROPIC_MODEL` to `claude-sonnet-4-6`. The active model is determined by the environment variable at runtime. |
| No token refresh | JWTs expire after 60 minutes with no refresh mechanism. The user must re-authenticate. |
| Client-side pagination only | The `GET /api/events` endpoint returns all events for the user. Pagination (page size 10) is applied client-side in `DashboardPage`. |

## Assumptions

- The Nginx production proxy configuration is defined in `./nginx/prod.conf`. This file was referenced in `docker-compose.yml` but not readable at authoring time; its exact contents are assumed to match the description in `CLAUDE.md` (strips `/apis/pacevite/`, forwards `X-Forwarded-Prefix`).
- `Event.UserId` is stored as a `string` foreign-key value matching `AspNetUsers.Id`. Whether a database-level FK constraint is enforced depends on the EF Core migration (the migration file was not found under a predictable path and was not read). This is treated as an application-level constraint only unless confirmed by migration inspection.
- `Source` defaults to `"MANUAL"` for all uploads. No other source values are set by the current codebase.
- The `Anthropic__ApiKey` environment variable is validated by the container but the API itself does not fail fast at startup if it is absent (no `??` throw pattern observed for it in `Program.cs`).

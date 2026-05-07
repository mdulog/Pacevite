# Pacevite — Architecture

## Architectural Style

Pacevite uses **vertical slice architecture** on the backend. Each feature (e.g., `Upload`, `GetEvents`, `GetPersonalBests`, `Register`, `Login`) lives in its own folder under `Features/` and owns its command or query record, handler, validator, and endpoint registration. Nothing crosses feature boundaries except shared contracts in `Contracts/` and domain entities in `Domain/`.

The mediator is **source-generated** via martinothamar/Mediator. Handlers are discovered at compile time; adding a new `ICommand`/`IQuery` implementation requires a build before the source-generated dispatcher recognises it.

The frontend uses a **feature-per-page** approach with shared hooks and a centralised server-state cache (TanStack Query). There is no Redux or Zustand store; all remote data lives in the query cache.

## Backend Layer Breakdown

```
src/Pacevite.Api/
├── Domain/                 # Pure domain types — no DI, no EF references
│   ├── Entities/           # Event, EventSplit
│   └── Enums/              # EventType, CompletionStatus
│
├── Contracts/              # DTOs crossing the API boundary
│   ├── Requests/           # RegisterRequest, LoginRequest (JSON body)
│   └── Responses/          # AuthResponse, EventResponse, EventSplitResponse,
│                           # PersonalBestResponse
│
├── Features/               # Vertical slices — one folder per feature
│   ├── Auth/
│   │   ├── AuthResult.cs   # Discriminated union (Ok / Fail / FailDuplicate)
│   │   ├── AuthEndpoints.cs
│   │   ├── Register/       # RegisterCommand, RegisterHandler, RegisterValidator
│   │   └── Login/          # LoginCommand, LoginHandler
│   └── Events/
│       ├── EventEndpoints.cs
│       ├── EventMapper.cs  # Entity → response DTO mapping
│       ├── Upload/         # UploadEventCommand, UploadEventHandler, UploadEventValidator
│       ├── GetEvents/      # GetEventsQuery, GetEventsHandler
│       ├── GetEventById/   # GetEventByIdQuery, GetEventByIdHandler
│       ├── GetPersonalBests/ # GetPersonalBestsQuery, GetPersonalBestsHandler
│       └── DeleteEvent/    # DeleteEventCommand, DeleteEventHandler
│
├── Pipeline/               # Cross-cutting Mediator behaviors
│   ├── ValidationBehavior.cs      # Runs FluentValidation before every handler
│   └── ValidationExceptionHandler.cs  # Converts ValidationException → RFC 7807 400
│
└── Infrastructure/         # I/O adapters and external integrations
    ├── Auth/               # IJwtTokenService, JwtTokenService
    ├── Chat/               # IChatToolHandler, IChatToolExecutor, ChatToolExecutor
    │                       # (scaffolded — not registered in DI)
    ├── OpenApi/            # ForwardedPrefixTransformer
    ├── Parsing/            # IEventParser, ParsedEvent, ParsedSplit,
    │                       # CsvEventParser, JsonEventParser
    └── Persistence/        # AppDbContext (EF Core + Identity)
```

### Layer Responsibilities

| Layer | Responsibility | May depend on |
|---|---|---|
| Domain | Entity definitions, enum values | Nothing |
| Contracts | Request/response DTOs for the HTTP boundary | Nothing |
| Features | Business logic, DB queries via EF Core, handler orchestration | Domain, Contracts, Infrastructure |
| Pipeline | Cross-cutting validation, exception conversion | FluentValidation, Mediator |
| Infrastructure | I/O implementations (JWT, parsing, persistence, OpenAPI) | Domain |

**Rule:** Features may reference Infrastructure directly (e.g., `AppDbContext`, `IEventParser`). Infrastructure must never reference Features.

## Frontend Layer Breakdown

```
src/Pacevite.Web/src/
├── App.tsx             # Router, QueryClient, ThemeProvider, AuthProvider mount
├── pages/             # One component per route
│   ├── LoginPage.tsx
│   ├── RegisterPage.tsx
│   ├── DashboardPage.tsx
│   ├── UploadPage.tsx
│   └── EventDetailPage.tsx
│
├── components/        # Reusable UI components
│   ├── AuthGuard.tsx       # Redirects unauthenticated users to /login
│   ├── ThemeToggle.tsx     # Light/dark switch button
│   ├── ProgressChart.tsx   # Recharts LineChart — finish-time trend
│   ├── PbPanel.tsx         # PB selector + progress bars
│   ├── SplitChart.tsx      # Recharts BarChart — split vs average delta
│   └── RaceComparison.tsx  # Recharts spark line + stats vs average
│
├── context/
│   ├── AuthContext.tsx     # Holds user identity; delegates token to tokenStore
│   └── ThemeContext.tsx    # Theme state + OS preference detection
│
├── hooks/
│   ├── useAuth.ts          # AuthContext accessor
│   ├── useEvents.ts        # TanStack Query over GET /api/events
│   └── useEvent.ts         # TanStack Query over GET /api/events/{id}
│
└── lib/
    ├── api.ts              # Axios client, tokenStore, Bearer interceptor
    ├── types.ts            # TypeScript interfaces matching API contracts
    └── chartUtils.ts       # Pure functions: groupByEventType, computePbs,
                            # computeAverageSplits, computeSplitDeltas, formatElapsed
```

## Dependency Flow

```
HTTP request
  └─→ Minimal API endpoint (EventEndpoints / AuthEndpoints)
        └─→ IMediator.Send(command/query)
              └─→ ValidationBehavior<TMessage, TResponse>
                    └─→ IValidator<TMessage>.Validate()   ← FluentValidation
                          [throws ValidationException if invalid]
                    └─→ Handler.Handle()
                          ├─→ AppDbContext (EF Core → PostgreSQL)
                          └─→ IEventParser / IJwtTokenService (Infrastructure)
```

Validation failures surface as `ValidationException` which `ValidationExceptionHandler` converts to an RFC 7807 `ValidationProblemDetails` response (HTTP 400). The endpoint never catches this — it propagates up the middleware pipeline.

## Request Lifecycle

### Typical query: GET /api/events

1. Nginx (prod) or Vite proxy (dev) forwards `GET /api/events` to the API.
2. `UseAuthentication` validates the JWT from the `Authorization: Bearer` header. Requests without a valid token are rejected at the middleware level (401).
3. `EventEndpoints.GetEventsAsync` extracts `UserId` from `ClaimTypes.NameIdentifier`.
4. `IMediator.Send(new GetEventsQuery(userId, eventType, from, to))` is called.
5. `ValidationBehavior` checks for registered validators. `GetEventsQuery` has no validator, so it passes through immediately.
6. `GetEventsHandler` runs: filters `db.Events` by `UserId`, optionally by `EventType` and date range (validated via `Enum.TryParse` — invalid values are silently ignored), orders by `EventDate` descending, eagerly loads `Splits`.
7. `EventMapper.ToResponse` converts each entity to `EventResponse` (enum strings are uppercased).
8. Handler returns `IReadOnlyList<EventResponse>`. Mediator forwards to endpoint. Endpoint returns `TypedResults.Ok(result)`.

### Typical command: POST /api/events/upload

1. Endpoint extracts `UserId` from claims, opens the `IFormFile` stream.
2. `IMediator.Send(new UploadEventCommand(userId, contentType, stream))`.
3. `ValidationBehavior` runs `UploadEventValidator`: validates non-empty userId, content type must start with `text/csv` or `application/json`, file must be non-empty and ≤ 10 MB.
4. `UploadEventHandler` selects the first `IEventParser` where `CanParse(contentType)` returns `true`.
5. Parser produces `IReadOnlyList<ParsedEvent>`.
6. Handler loads existing keys for the user (`UserId + EventType + EventName + EventDate`) as a hash set.
7. For each `ParsedEvent`: validate `EventType` and `Completion` against enums (skip with warning if invalid); skip if a duplicate key is found. Otherwise create `Event` and related `EventSplit` entities.
8. `db.SaveChangesAsync` persists all entities in a single round-trip.
9. Returns `IReadOnlyList<EventResponse>` for all newly created events.

## Auth Flow

### Registration

```
POST /api/auth/register  { email, password }
  → RegisterValidator (email format, password length 8–100)
  → RegisterHandler
      → UserManager.FindByEmailAsync (duplicate check)
      → UserManager.CreateAsync (ASP.NET Identity — hashes password)
      → JwtTokenService.GenerateToken
          → HMAC-SHA256 JWT, claims: sub=userId, email, jti
          → Expires: UtcNow + 60 minutes (hardcoded constant)
  ← 201 Created  { userId, email, token }
  ← 409 Conflict (duplicate email)
  ← 400 Bad Request (validation failure)
```

### Login

```
POST /api/auth/login  { email, password }
  → LoginHandler
      → UserManager.FindByEmailAsync
      → UserManager.CheckPasswordAsync
      → JwtTokenService.GenerateToken
  ← 200 OK  { userId, email, token }
  ← 401 Unauthorized (bad credentials — email existence not revealed)
```

### Frontend token lifecycle

1. `AuthContext.login(userId, email, token)` calls `tokenStore.set(token)` (module-level variable in `lib/api.ts` — never written to `localStorage` or `sessionStorage`).
2. `apiClient` Axios request interceptor reads `tokenStore.get()` and adds `Authorization: Bearer <token>` to every outbound request.
3. `AuthContext.logout()` calls `tokenStore.clear()` and sets `user` to `null`, which triggers `AuthGuard` to redirect to `/login` on the next navigation.
4. The token has a 60-minute server-side lifetime. There is no refresh mechanism; users must re-authenticate after expiry.

### Route protection

`AuthGuard` wraps all protected routes. If `isAuthenticated` is false it renders `<Navigate to="/login" replace />`. `isAuthenticated` is derived from `user !== null` in `AuthContext`.

## Parser Dispatch

All `IEventParser` implementations are registered as singletons in `Program.cs` (currently `CsvEventParser` and `JsonEventParser`). `UploadEventHandler` receives `IEnumerable<IEventParser>` by DI injection and dispatches:

```csharp
var parser = parsers.FirstOrDefault(p => p.CanParse(command.ContentType))
    ?? throw new InvalidOperationException(...);
```

`CsvEventParser.CanParse` matches any `contentType` starting with `"text/csv"` (case-insensitive). `JsonEventParser.CanParse` matches `"application/json"`. Adding a new format requires: implementing `IEventParser`, registering it in `Program.cs`, and adding a `CanParse` guard — no existing code changes.

`EventType` and `Completion` values are uppercased by the parsers before returning `ParsedEvent`. The handler then parses them with `Enum.TryParse(ignoreCase: true)`.

## Chat Tool Plugin Pattern (Scaffolded)

The infrastructure for AI-powered coaching is stubbed out but not wired to any endpoint or registered in DI.

```
IChatToolHandler           — one implementation per Anthropic tool call
  ValueTask<string> ExecuteAsync(JsonNode input, string userId, CancellationToken ct)

IChatToolExecutor          — dispatches by tool name
  ValueTask<string> ExecuteAsync(string toolName, JsonNode input, string userId, CancellationToken ct)

ChatToolExecutor           — concrete implementation
  • Receives IReadOnlyDictionary<string, IChatToolHandler>
  • Dispatches to the matching handler by name
  • Logs unknown tool names at Warning; logs handler exceptions at Critical
```

When this feature is activated, a new endpoint would send conversation history to the Anthropic SDK, receive tool-call requests, route them through `ChatToolExecutor`, and feed results back to Anthropic for a final response. The Anthropic model is set via the `ANTHROPIC_MODEL` environment variable (defaults to `claude-sonnet-4-6` in `docker-compose.yml`; `CLAUDE.md` names `claude-haiku-4-5-20251001`).

## Development vs Production Topology

### Development

```
Browser → Vite dev server (:5173) → /api/* proxy → ASP.NET Core API (:5291)
                                                       ↓
                                                  PostgreSQL (:5432)
```

The Vite proxy (`vite.config.ts`) rewrites `/api` requests to `http://localhost:5291`. CORS is not required because the browser sees a single origin. EF Core runs `Database.Migrate()` automatically at startup (`Development` environment only).

### Production

```
Browser → Nginx (:8080) → /apis/pacevite/* → ASP.NET Core API (:5291)
              ↓
        Static Vite build (served by a second Nginx container)
                                                   ↓
                                              PostgreSQL (:5432)
```

Nginx strips the `/apis/pacevite/` path prefix and forwards `X-Forwarded-Prefix` so `ForwardedPrefixTransformer` can rewrite the OpenAPI server URL for Scalar. The API's `UseForwardedHeaders` middleware is restricted to RFC-1918 private networks (10/8, 172.16/12, 192.168/16) and loopback addresses to prevent spoofed `X-Forwarded-*` headers from public clients.

Auth endpoints are rate-limited to 10 requests/minute per the `"auth"` fixed-window policy. This limit is configurable via `RateLimit:Auth:PermitLimit`.

## Dark Mode Architecture

```
ThemeContext (React context)
  ├─ resolveInitialTheme() — checks localStorage, falls back to prefers-color-scheme
  ├─ useEffect: toggles class "dark" on document.documentElement
  ├─ useEffect: listens for OS changes (only when no localStorage override)
  └─ toggleTheme() — writes next theme to localStorage, updates state

ThemeToggle component — calls toggleTheme(), renders Sun/Moon icon

Chart components (ProgressChart, SplitChart, RaceComparison)
  └─ useTheme()  ← subscribes to ThemeContext, forces re-render on change
  └─ getComputedStyle(document.documentElement)
       → reads --color-secondary, --color-surface, --color-muted from CSS vars
       → passes values as tick/tooltip props to Recharts
```

Tailwind v4 generates dark-mode CSS variables. By calling `useTheme()` in each chart component without consuming its return value, the component re-renders whenever the theme changes and re-reads the CSS custom properties from the live document style.

## Component Hierarchy and Routing

```
App
├─ ThemeProvider
│   └─ QueryClientProvider
│       └─ AuthProvider
│           └─ RouterProvider
│               ├─ /login              → LoginPage
│               ├─ /register           → RegisterPage
│               ├─ /dashboard          → AuthGuard → DashboardPage
│               │                          ├─ ProgressChart
│               │                          ├─ PbPanel
│               │                          └─ (personal bests grid, event table)
│               ├─ /upload             → AuthGuard → UploadPage
│               ├─ /events/:id         → AuthGuard → EventDetailPage
│               │                          ├─ SplitChart
│               │                          └─ RaceComparison
│               ├─ /                   → Navigate /dashboard
│               └─ *                   → Navigate /dashboard
```

`AuthGuard` performs client-side route protection. All API calls are secured at the server level by JWT bearer authentication.

## State Management Approach

| Data category | Storage |
|---|---|
| Server data (events, PBs) | TanStack Query cache (`queryKey: ['events']`, `['personal-bests']`, `['event', id]`) |
| Authentication state | `AuthContext` React state + `tokenStore` module variable |
| Theme preference | `ThemeContext` React state + `localStorage` |
| UI state (search, pagination, selected type) | Local `useState` in `DashboardPage` |

TanStack Query is configured with `staleTime: 30_000` ms and `retry: 1`. Mutations that modify events (`upload`, `delete`) call `queryClient.invalidateQueries` for both `['events']` and `['personal-bests']` to keep the cache consistent.

## Key Patterns and Rationale

| Pattern | Where | Rationale |
|---|---|---|
| Vertical slice architecture | `Features/` | Keeps all code for a feature co-located; avoids cross-feature coupling |
| Source-generated Mediator | All commands/queries | Zero-overhead dispatch vs reflection-based mediators; enforces single-handler-per-message |
| `AuthResult` discriminated union | `Features/Auth/` | Maps auth outcomes (success, duplicate, bad credentials) to specific HTTP status codes without throwing exceptions for expected business failures |
| `IEventParser` strategy | `Infrastructure/Parsing/` | Open/Closed: new file formats are added as new implementations, no existing code changes |
| `IChatToolHandler` plugin | `Infrastructure/Chat/` | OCP + ISP: each tool is a focused, independently testable class; `ChatToolExecutor` dispatches by name |
| In-memory JWT (`tokenStore`) | `lib/api.ts` | Prevents XSS from reading the token via `localStorage` |
| CSS var dark mode | `ThemeContext` + chart components | Single source of truth in CSS; Recharts cannot read Tailwind classes directly so CSS vars bridge the gap |

## Architectural Decisions Worth Capturing as ADRs

The following decisions are implemented in the codebase but do not yet have corresponding ADR documents in `docs/decisions/`:

1. **Vertical slice architecture over layered architecture** — feature folders prevent horizontal coupling across domain areas.
2. **Source-generated Mediator over MediatR** — avoids reflection; compile-time handler discovery.
3. **JSONB columns for `Location` and `Metadata`** — avoids per-event-type schema migrations; enables GIN-indexed querying.
4. **In-memory JWT storage** — trades session persistence across page reloads for XSS resistance.
5. **`AuthResult` discriminated union** — avoids exception-driven flow for expected business outcomes.
6. **No navigation property from `Event.UserId` to `IdentityUser`** — keeps Identity schema changes from leaking into event queries (see `02-data-model.md`).

## Assumptions

- The `ValidationBehavior` is the sole cross-cutting concern in the Mediator pipeline. No logging, tracing, or caching behaviors are registered.
- `GetEventsQuery` has no `FluentValidation` validator. The absence of a validator file was confirmed; the behavior (silent fallback to unfiltered results on invalid `eventType`) is documented as a known gap.
- The production Nginx proxy configuration (`./nginx/prod.conf`) is assumed to strip `/apis/pacevite/` and forward `X-Forwarded-Prefix`. The file was referenced in `docker-compose.yml` but not read at authoring time.
- Pagination is client-side only. The `GET /api/events` endpoint returns all user events in a single response with no server-side page/cursor parameters.
- There is no CORS configuration in `Program.cs`. The Vite proxy in development and the Nginx proxy in production ensure the browser sees a single origin.
- `LoginCommand` has no FluentValidation validator. Input validation for login relies on Identity's `CheckPasswordAsync` returning false for incorrect credentials.

# Refresh Tokens — Design Spec

**Date:** 2026-05-01
**Status:** Approved
**ADR:** [0007 — Store Refresh Tokens in a Standalone Table](../decisions/0007-standalone-refresh-tokens-table.md)

---

## Overview

Add a refresh token mechanism to the Pacevite auth flow. The 60-minute JWT access token is supplemented by a 7-day httpOnly cookie-based refresh token. When the access token expires, an Axios response interceptor silently exchanges the cookie for a new access token — the user never sees a login prompt during an active session. Logging out revokes the refresh token server-side and clears the cookie.

---

## Data Layer

### `RefreshToken` entity (`Domain/Entities/RefreshToken.cs`)

| Column | Type | Nullable | Notes |
|---|---|---|---|
| `Id` | `uuid` | NOT NULL | PK, `Guid.NewGuid()` |
| `UserId` | `string` | NOT NULL | App-level ownership — no DB FK (consistent with ADR 0006) |
| `TokenHash` | `string` | NOT NULL | SHA-256 of the raw token — raw token never stored |
| `ExpiresAt` | `timestamptz` | NOT NULL | `CreatedAt + 7 days` |
| `CreatedAt` | `timestamptz` | NOT NULL | UTC |
| `RevokedAt` | `timestamptz` | NULL | Null = active; set on logout or rotation |
| `ReplacedByTokenHash` | `string` | NULL | Set when rotated — supports future reuse detection |

**Indexes:** unique on `TokenHash`; composite on `(UserId, RevokedAt)` for active-token lookups.

### EF Core configuration

Configured via Fluent API in `AppDbContext.OnModelCreating`. No data annotations on the entity.

### Migration

One new migration: `AddRefreshTokens`. Adds the `RefreshTokens` table — no changes to existing tables.

---

## Auth Service Layer

### `IJwtTokenService` — two new methods added to the existing interface

```csharp
string GenerateToken(IdentityUser user);     // existing — access JWT
string GenerateRefreshToken();               // crypto-random 64 bytes, base64url encoded
string HashToken(string rawToken);           // SHA-256 — used before storing or looking up
```

Access token lifetime changes from hardcoded 60 min → configurable via `Jwt:AccessTokenExpiryMinutes` (default: `15`). With silent refresh in place, a shorter window limits the blast radius of a leaked access token.

### `IRefreshTokenService` — new interface (`Infrastructure/Auth/`)

Separated from `IJwtTokenService` to respect ISP: token generation and token lifecycle are distinct concerns.

```csharp
Task<string> CreateAsync(string userId, CancellationToken ct);
Task<(bool Valid, string? NewRawToken)> RotateAsync(string rawToken, CancellationToken ct);
Task RevokeAsync(string rawToken, CancellationToken ct);
```

- **`CreateAsync`** — generates a raw token, hashes it, persists the `RefreshToken` row, returns the raw token to the caller (endpoint sets the cookie).
- **`RotateAsync`** — finds the row by hash; returns `(false, null)` if not found, expired, or already revoked; otherwise marks old row as revoked (sets `RevokedAt` + `ReplacedByTokenHash`), creates a new row, returns `(true, newRawToken)`.
- **`RevokeAsync`** — finds the active row by hash, sets `RevokedAt`. No-ops if not found (idempotent).

---

## Backend Features

### Updated: Login + Register handlers

After generating the access JWT, each handler calls `IRefreshTokenService.CreateAsync`. The raw refresh token is added to `AuthResult`. The endpoint reads it from the result and writes the httpOnly cookie — handlers never touch `HttpResponse` directly.

`AuthResponse` contract is **unchanged** — the refresh token travels only in the cookie, never in the JSON body.

### New: `Features/Auth/Refresh/`

**Endpoint:** `POST /api/auth/refresh` — anonymous, under the `"auth"` rate-limit policy.

- Reads the `refreshToken` cookie from the request.
- Sends `RefreshCommand(rawToken)` to `RefreshHandler`.
- Handler calls `IRefreshTokenService.RotateAsync`.
- On success: issues new access JWT, sets new refresh cookie, returns `200 { token }`.
- On failure (missing/expired/revoked): returns `401 Unauthorized`, clears cookie.

No `FluentValidation` validator needed — the only input is the cookie value, validated by `RotateAsync`.

### New: `Features/Auth/Logout/`

**Endpoint:** `POST /api/auth/logout` — requires `Authorization: Bearer` (validates session before revoking).

- Reads the `refreshToken` cookie.
- Sends `LogoutCommand(rawToken?)` to `LogoutHandler`.
- Handler calls `IRefreshTokenService.RevokeAsync` (no-ops if cookie absent).
- Endpoint clears the cookie regardless, returns `204 No Content`.

### Cookie configuration

Applied consistently on login, register, and refresh responses; cleared on logout.

| Attribute | Value | Reason |
|---|---|---|
| `HttpOnly` | `true` | JS cannot read the token — XSS resistance |
| `Secure` | `true` (prod) / `false` (dev) | HTTPS only in production |
| `SameSite` | `Strict` | CSRF protection for same-origin SPA |
| `Path` | `/api/auth` | Cookie only sent to auth endpoints, not every API call |
| `MaxAge` | 7 days | Matches DB expiry |

---

## Frontend

### `lib/api.ts` — response interceptor

A response interceptor is added alongside the existing request interceptor.

**On 401:**
1. If `_isRetry` flag is set on the original request config → already retried after a refresh; call `AuthContext.logout()` and reject.
2. If a refresh is already in flight → queue the request; resolve/reject it when the refresh completes.
3. Otherwise: set `isRefreshing = true`, call `POST /api/auth/refresh` (browser sends cookie automatically).
   - **Success (200):** update `tokenStore` with new access token, resolve queued requests, retry original with new token.
   - **Failure (4xx):** call `AuthContext.logout()` (clears `tokenStore` + `user` state), reject queued requests. `AuthGuard` redirects to `/login`.

The queue prevents N parallel 401s from firing N parallel refresh calls — only one refresh executes; all others wait.

### `AuthContext.logout()`

Changes from client-only teardown to a two-step sequence:

1. `POST /api/auth/logout` — server revokes the refresh token and clears the cookie.
2. `tokenStore.clear()` + set `user` to `null`.

If the logout request fails (network error, already-expired token), client state is cleared anyway — the user is logged out locally even if the server call fails.

### No other frontend changes

`tokenStore`, `useEvents`, `useEvent`, all pages and chart components are unaffected.

---

## Error Handling Summary

| Scenario | Behaviour |
|---|---|
| Refresh token cookie missing on `/refresh` | `401`, cookie cleared |
| Refresh token expired | `401`, cookie cleared |
| Refresh token already revoked | `401`, cookie cleared |
| Access token expired, silent refresh succeeds | Original request retried transparently |
| Access token expired, refresh fails | `AuthContext.logout()`, redirect to `/login` |
| Logout with no cookie | Server no-ops, `204` returned, client state cleared |
| Concurrent 401s during refresh | Queued; retried after single refresh resolves |

---

## Out of Scope

- **Refresh token reuse detection** (chain revocation on replay) — `ReplacedByTokenHash` is stored to enable this in future, but the current implementation does not act on it.
- **Token cleanup job** — expired and revoked rows accumulate; a background sweep is not part of this implementation.
- **"Remember me" toggle** — all sessions get a 7-day refresh token; per-session lifetime configuration is not included.

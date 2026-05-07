# JWT Refresh Token Feature — Design Spec

**Date:** 2026-05-02
**Status:** Approved
**Author:** AI-assisted spec

---

## 1. Overview

Add a token refresh mechanism to Pacevite. When a user's 60-minute access JWT expires, the frontend silently exchanges a long-lived refresh token (stored in an httpOnly cookie) for a new access JWT — without requiring re-authentication.

The refresh token is single-use and rotates on every successful exchange. The backend stores a SHA-256 hash of the token in a new `RefreshTokens` table (not in `AspNetUserTokens` — see Decision below).

---

## 2. Goals

- Eliminate forced re-authentication after 60 minutes for active sessions
- Keep the in-memory `tokenStore` pattern for the access JWT (XSS safety, ADR 0004)
- Store the refresh token in an httpOnly, SameSite=Strict cookie (inaccessible to JS)
- Rotate refresh tokens on every use (single-use — revoke old, issue new)
- Revoke all refresh tokens on explicit logout
- Design that survives the AOT migration (no dependency on `AspNetUserTokens`)

---

## 3. Non-Goals

- Refresh token persistence across server restarts (a restart invalidates all sessions — acceptable)
- Sliding-window refresh (tokens expire after a fixed absolute duration, not activity-based)
- Refresh token revocation by device/session name
- Multi-device session management UI

---

## 4. Architecture Decision — Standalone `RefreshTokens` Table

**Why not `AspNetUserTokens`?**

`AspNetUserTokens` is part of ASP.NET Identity's schema. ADR 0006 documents the deliberate decision to minimise coupling between the application schema and the Identity schema. The AOT migration plan (`2026-04-11-aot-support.md`) will replace `IdentityDbContext` with a custom `AppDbContext` and drop the Identity tables entirely. Storing refresh tokens in `AspNetUserTokens` would require a migration at AOT time.

A standalone `RefreshTokens` table owned by the application schema is compatible with both the current Identity setup and the post-AOT `IUserRepository` world.

---

## 5. Data Model

### New table: `RefreshTokens`

| Column | Type | Nullable | Notes |
|---|---|---|------|
| `Id` | `uuid` | NOT NULL | PK, `Guid.NewGuid()` |
| `UserId` | `text` | NOT NULL | Application-level FK to `AspNetUsers.Id` (no DB constraint, follows ADR 0006) |
| `TokenHash` | `text` | NOT NULL | SHA-256 hex of the raw token — raw token never stored |
| `ExpiresAt` | `timestamp with time zone` | NOT NULL | UTC; default 7 days from creation |
| `RevokedAt` | `timestamp with time zone` | NULL | Set on use (rotation) or logout |
| `CreatedAt` | `timestamp with time zone` | NOT NULL | UTC timestamp |
| `ReplacedByTokenHash` | `text` | NULL | Hash of the successor token; set on rotation |

**Indexes:**

| Name | Columns | Purpose |
|---|---|---|
| `PK_RefreshTokens` | `Id` | Primary key |
| `IX_RefreshTokens_TokenHash` | `TokenHash` | Lookup by incoming cookie value |
| `IX_RefreshTokens_UserId` | `UserId` | Revoke-all-for-user on logout |

---

## 6. Token Generation

- Raw token: `Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))` — 64 random bytes, base64-encoded (~88 chars)
- Stored hash: `Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)))` — never store raw
- Cookie: `HttpOnly=true`, `Secure=true` (prod), `SameSite=Strict`, `Path=/api/auth`, `Max-Age=7d`
- Access JWT lifetime: unchanged at 60 minutes (configurable via `Jwt:ExpiryMinutes` — see Known Gap in 00-overview.md)

---

## 7. Endpoint Contract

### Modified: `POST /api/auth/login` and `POST /api/auth/register`

**Existing behaviour preserved.** Response body unchanged (`{ userId, email, token }`).

**New:** Sets `Set-Cookie: refresh_token=<raw>; HttpOnly; Secure; SameSite=Strict; Path=/api/auth; Max-Age=604800` in the response.

---

### New: `POST /api/auth/refresh`

No request body. Reads the `refresh_token` cookie.

**Success (200 OK):**
```json
{ "token": "<new access JWT>" }
```
Sets a new `Set-Cookie` with the rotated refresh token.

**Failure cases:**

| Condition | Status | Body |
|---|---|---|
| Cookie missing | 401 | `{ "title": "Unauthorized" }` |
| Token not found (hash not in DB) | 401 | `{ "title": "Unauthorized" }` |
| Token expired | 401 | `{ "title": "Unauthorized" }` |
| Token already revoked | 401 | `{ "title": "Unauthorized" }` |

All failure responses are identical to prevent token oracle attacks.

---

### Modified: `POST /api/auth/logout` *(new endpoint)*

No request body. Reads the `refresh_token` cookie.

**Success (204 No Content):** Revokes the token in DB. Clears the cookie via `Set-Cookie: refresh_token=; Max-Age=0`.

**Cookie missing:** Still returns 204 — idempotent.

---

## 8. Backend Design

### New Files

```
src/Pacevite.Api/
├── Domain/
│   └── Entities/
│       └── RefreshToken.cs           # Entity
├── Features/
│   └── Auth/
│       ├── Refresh/
│       │   ├── RefreshCommand.cs     # ICommand<AuthResult>
│       │   └── RefreshHandler.cs    # rotates token, issues new JWT
│       └── Logout/
│           ├── LogoutCommand.cs      # ICommand
│           └── LogoutHandler.cs     # revokes all tokens for user
├── Infrastructure/
│   └── Auth/
│       ├── IRefreshTokenService.cs  # Generate, Validate, Revoke, RevokeAll
│       └── RefreshTokenService.cs  # EF Core implementation
```

### Modified Files

```
src/Pacevite.Api/
├── Domain/Entities/ (no change — RefreshToken is new)
├── Infrastructure/Persistence/
│   └── AppDbContext.cs               # Add DbSet<RefreshToken>
├── Features/Auth/
│   └── AuthEndpoints.cs              # Register /refresh and /logout routes;
│                                     # modify /login and /register to set cookie
├── Contracts/Responses/
│   └── RefreshResponse.cs            # { string Token }
└── Program.cs                        # Register IRefreshTokenService
```

### `IRefreshTokenService`

```csharp
public interface IRefreshTokenService
{
    // Creates a new refresh token row; returns the raw (unhashed) token
    Task<string> GenerateAsync(string userId, CancellationToken ct);

    // Looks up by hash; returns UserId if valid (not expired, not revoked), null otherwise
    Task<string?> ValidateAndRotateAsync(string rawToken, CancellationToken ct);

    // Revokes a single token by raw value (used on explicit logout of current device)
    Task RevokeAsync(string rawToken, CancellationToken ct);

    // Revokes all tokens for a user (used on logout-all / password change)
    Task RevokeAllForUserAsync(string userId, CancellationToken ct);
}
```

### Cookie Helper

A static `CookieHelper.SetRefreshTokenCookie(HttpResponse, string rawToken, bool isProduction)` sets the cookie with the correct flags. The `IsDevelopment` flag comes from `IWebHostEnvironment` injected into `AuthEndpoints`. In development, `Secure=false` so the cookie works over plain HTTP.

---

## 9. Frontend Design

### Modified: `lib/api.ts`

The Axios instance gains a **response interceptor** that:
1. Detects a 401 response on any request that is **not** `/api/auth/refresh` or `/api/auth/login`
2. Calls `POST /api/auth/refresh` (cookie is sent automatically by the browser)
3. On success: calls `tokenStore.set(newToken)` and retries the original request once
4. On failure (refresh also 401): calls `AuthContext.logout()` → redirect to `/login`

The refresh call itself uses `fetch` (not the Axios instance) to avoid infinite interceptor recursion.

### Modified: `context/AuthContext.tsx`

`logout()` now calls `POST /api/auth/logout` before clearing `tokenStore` and `user` state, so the server-side cookie and DB record are cleaned up.

### No localStorage

The refresh token lives exclusively in the httpOnly cookie — never in JS-accessible storage. This is consistent with ADR 0004 [cite:file:5].

---

## 10. Security Model

| Control | Implementation |
|---|---|
| Refresh token never stored raw | SHA-256 hash stored in DB; raw token only in the cookie |
| Single-use rotation | On each `/refresh` call: current token is revoked, new token issued; `ReplacedByTokenHash` links them |
| httpOnly cookie | Inaccessible to JavaScript; immune to XSS token theft |
| SameSite=Strict | Cookie not sent on cross-origin requests; CSRF protection |
| Path=/api/auth | Cookie only sent to auth endpoints, not `/api/events` etc. |
| Logout revokes DB record | Token is unusable even if cookie is somehow captured |
| 401 oracle prevention | All invalid-token scenarios return identical 401 with no distinguishing detail |
| No User-Agent / IP binding | Avoids false-positive revocations from mobile IP changes; tradeoff is accepted |

---

## 11. Error Handling

| Scenario | Behaviour |
|---|---|
| `/refresh` called with missing/invalid cookie | 401 — frontend interceptor calls `logout()` |
| `/refresh` called with expired token | 401 — same |
| `/refresh` called with revoked token (replay attack) | 401 — token family could be revoked here in a future hardening pass |
| DB unavailable during refresh | 500 — frontend shows re-login prompt |
| `/logout` called without cookie | 204 — idempotent, no error |

---

## 12. Testing

### Unit Tests (`tests/Pacevite.Api.Tests/Unit/Auth/`)

- `RefreshTokenServiceTests` — Generate creates hashed row; ValidateAndRotate returns UserId for valid token; returns null for expired; returns null for revoked; sets RevokedAt and ReplacedByTokenHash on rotation
- `RefreshHandlerTests` — returns new JWT on valid token; returns AuthResult.Fail on invalid token
- `LogoutHandlerTests` — calls RevokeAsync; returns success even if token not found

### Integration Tests (`tests/Pacevite.Api.Tests/Integration/`)

- `POST /api/auth/login` — response includes `Set-Cookie` with `refresh_token`
- `POST /api/auth/refresh` — valid cookie returns 200 with new JWT and new cookie
- `POST /api/auth/refresh` — no cookie returns 401
- `POST /api/auth/refresh` — expired token returns 401
- `POST /api/auth/refresh` — replayed (already-used) token returns 401
- `POST /api/auth/logout` — clears cookie; subsequent `/refresh` with old cookie returns 401

### Frontend Unit Tests

- `useChatStream` / Axios interceptor — 401 triggers refresh; successful refresh retries original request
- `useChatStream` / Axios interceptor — failed refresh calls `logout()` and clears auth state
- `AuthContext.logout()` — calls `POST /api/auth/logout` before clearing state

---

## 13. Configuration

No new required environment variables. Optional:

| Variable | Default | Purpose |
|---|---|---|
| `Jwt:RefreshTokenExpiryDays` | `7` | Refresh token lifetime in days |

---

## 14. Migration

One new EF Core migration: `AddRefreshTokens`

```bash
dotnet ef migrations add AddRefreshTokens --project src/Pacevite.Api
```

The migration creates the `RefreshTokens` table and its three indexes. No changes to existing tables.

---

## 15. ADR

This feature introduces one new architectural decision that should be captured as ADR 0007:

**0007 — Store Refresh Tokens in a Standalone Table**

Chosen over `AspNetUserTokens` to avoid coupling the refresh token lifecycle to the Identity schema, which is being removed in the AOT migration.

# 0004 — Store JWT in Memory, Not localStorage

**Status:** Accepted

## Context and Problem Statement

The frontend needs to persist the JWT between requests. The common options are `localStorage`, `sessionStorage`, an httpOnly cookie, or an in-memory module variable.

## Considered Options

1. **`localStorage`** — survives page reload, accessible via JS
2. **`sessionStorage`** — survives tab lifetime, accessible via JS
3. **httpOnly cookie** — inaccessible to JS, requires CSRF protection
4. **In-memory module variable (`tokenStore`)** — lives only in the current JS module, inaccessible outside it

## Decision Outcome

Chose **in-memory `tokenStore`** — a module-level variable in `lib/api.ts` with `get()`, `set()`, and `clear()` methods. The Axios request interceptor reads it and injects `Authorization: Bearer <token>` on every outbound request.

**Why:**
- `localStorage` and `sessionStorage` are readable by any script on the page — a single XSS vulnerability exposes the token. The OWASP recommendation is to never store tokens in Web Storage.
- An httpOnly cookie would require CORS and CSRF protection wiring across the Nginx proxy and the API — significant complexity for a single-origin SPA.
- The in-memory approach is the simplest option that eliminates the XSS risk.

## Consequences

- **Easier:** XSS resistance — a compromised script cannot read the token from storage.
- **Harder:** The token does not survive a page reload — users must re-authenticate after refreshing. This is acceptable for the current use case (personal fitness tool, 60-minute JWT lifetime).
- There is no refresh mechanism. If token refresh is added in the future, it will need a separate httpOnly cookie for the refresh token.

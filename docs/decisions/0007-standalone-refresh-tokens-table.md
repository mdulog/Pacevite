# 0007 — Store Refresh Tokens in a Standalone Table

**Status:** Accepted

## Context and Problem Statement

Refresh tokens must be persisted server-side to enable rotation and revocation. The options are storing them in `AspNetUserTokens` (part of the ASP.NET Identity schema) or in a standalone `RefreshTokens` table owned by the application.

## Considered Options

1. **`AspNetUserTokens`** — built-in Identity table; no new migration needed
2. **Standalone `RefreshTokens` table** — application-owned; independent of Identity schema

## Decision Outcome

Chose **standalone `RefreshTokens` table**.

**Why:**
- ADR 0006 documents a deliberate policy to minimise coupling between the application schema and the Identity schema. `AspNetUserTokens` is Identity-managed; storing data there ties the refresh token lifecycle to Identity's schema evolution.
- The AOT migration plan (`2026-04-11-aot-support.md`) replaces `IdentityDbContext` with a custom `AppDbContext` and drops all Identity tables. A standalone `RefreshTokens` table survives this migration unchanged.
- A standalone table gives full control over columns (`ReplacedByTokenHash`, `RevokedAt`, custom indexes) that `AspNetUserTokens` does not support natively.

## Consequences

- **Easier:** Refresh token schema evolves independently of Identity. AOT migration does not touch this table.
- **Harder:** One additional migration vs. zero for `AspNetUserTokens`.
- Token cleanup (expired + revoked rows) requires a scheduled job or manual sweep — no automatic pruning.

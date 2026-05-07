# 0006 — No Database FK from Event.UserId to AspNetUsers

**Status:** Accepted

## Context and Problem Statement

`Event.UserId` stores the ID of the owning user as a `string` matching `AspNetUsers.Id`. The question is whether to enforce this as a database-level foreign key constraint with `HasOne<IdentityUser>().WithMany().HasForeignKey(e => e.UserId)`.

## Considered Options

1. **Database-level FK** — EF Core navigation property, enforced by PostgreSQL, cascade on delete
2. **Application-level constraint only** — `UserId` is stored as a string, ownership enforced by always filtering queries by `UserId` from claims

## Decision Outcome

Chose **application-level constraint only**. `Event` has no EF Core navigation property to `IdentityUser`. Ownership is enforced by always including `WHERE UserId = @userId` (from the JWT claim) in every query that reads or mutates events.

**Why:**
- A database FK from the event schema to the Identity schema creates a hard coupling between the two. Any future change to how Identity stores user IDs (e.g., switching to a custom `ApplicationUser`, changing ID type) would require a migration on the `Events` table.
- ASP.NET Identity manages its own schema (`AspNetUsers`, `AspNetRoles`, etc.). Tying event tables to it via FK makes the event schema a dependent of the Identity schema.
- The application-level constraint is sufficient: every query handler extracts `UserId` from the validated JWT claim and filters by it. An unauthenticated request never reaches a handler.

## Consequences

- **Easier:** The event schema evolves independently of the Identity schema.
- **Harder:** Deleting a user does not cascade-delete their events — a separate cleanup step or soft-delete strategy is needed if user deletion is added.
- There is no referential integrity guarantee at the DB level. If a `UserId` is stored that has no corresponding `AspNetUsers` row, the DB will not reject it. This is acceptable because user creation and event upload are both application-controlled operations.

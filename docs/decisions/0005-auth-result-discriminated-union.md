# 0005 — Use AuthResult Discriminated Union for Auth Outcomes

**Status:** Accepted

## Context and Problem Statement

Auth handlers (Register, Login) produce one of several distinct outcomes: success, invalid credentials, duplicate email. These outcomes map to different HTTP status codes. The question is how to communicate them from the handler back to the endpoint.

## Considered Options

1. **Throw domain exceptions** (`DuplicateEmailException`, `InvalidCredentialsException`) — caught at the endpoint or middleware
2. **Return `null` or a boolean** — caller must infer meaning
3. **`AuthResult` discriminated union** — a sealed type with `Ok`, `Fail`, and `FailDuplicate` cases

## Decision Outcome

Chose **`AuthResult` discriminated union** in `Features/Auth/AuthResult.cs`. Handlers return `AuthResult`; endpoints switch on the case to produce `201 Created`, `401 Unauthorized`, or `409 Conflict`.

**Why:**
- Exception-driven flow for expected business outcomes (wrong password, duplicate registration) conflates error handling with control flow. Exceptions are for unexpected failures; wrong credentials are an anticipated outcome.
- `null` or boolean returns lose information — the endpoint cannot distinguish "bad credentials" from "user not found" without re-querying.
- The discriminated union makes all possible outcomes explicit in the return type. Adding a new auth outcome is a compile-time change, not a runtime convention.

## Consequences

- **Easier:** Each auth outcome maps unambiguously to an HTTP status code; the endpoint code reads as a straightforward switch.
- **Harder:** Slightly more ceremony than throwing an exception — a new outcome requires updating the `AuthResult` type and every switch site.
- This pattern should be considered for future features that have multiple distinct failure modes (e.g., an upload that can fail due to unsupported format vs. duplicate data).

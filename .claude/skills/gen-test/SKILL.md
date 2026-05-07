---
name: gen-test
description: Generate the right test for a given source file, choosing the correct layer automatically based on file type and location.
---

Inspect the target file and choose the layer:

| File | Layer | Pattern to follow |
|---|---|---|
| `src/Pacevite.Web/src/**/*.tsx` (page) | Vitest + Testing Library | `DashboardPage.test.tsx` — use `renderWithProviders`, MSW server overrides, `userEvent` |
| `src/Pacevite.Web/src/**/*.tsx` (component) | Vitest + Testing Library | `AuthGuard.test.tsx` — render in isolation, assert DOM output |
| `src/Pacevite.Api/Features/**/` handler `.cs` | TUnit Unit + NSubstitute | `CsvEventParserTests.cs` — `[Category("Unit")]`, mock injected interfaces with `Substitute.For<T>()` |
| `src/Pacevite.Api/Features/**/` endpoint | TUnit Integration + Testcontainers | `AuthEndpointsTests.cs` — `[Category("Integration")]`, `[Before(Test)]`/`[After(Test)]`, real Postgres via `PostgreSqlBuilder` |
| `e2e/` user flow | Playwright E2E | `login.spec.ts` — use `uniqueEmail()`, `registerViaApi()`, `loginViaUi()` helpers from `e2e/helpers.ts` |

Rules:
- Place the test file alongside the source file (web) or in the matching `tests/` subdirectory (API)
- Always add `[Category("Unit")]` or `[Category("Integration")]` to TUnit test classes
- Use `[Before(Test)]` / `[After(Test)]` (not constructors) for TUnit setup/teardown
- Cover: happy path, at least one failure/edge case, and any branching logic
- Follow AAA structure with `// Arrange` / `// Act` / `// Assert` comments

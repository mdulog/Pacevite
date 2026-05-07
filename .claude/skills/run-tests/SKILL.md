---
name: run-tests
description: Run .NET unit, integration, or frontend tests using the correct invocations for this project. Args: unit | integration | all | frontend | e2e.
---

NEVER use `dotnet test` — TUnit on .NET 10 requires `dotnet run --project`.

## .NET Tests

Run from /home/madulog/Projects/Pacevite:

Unit only:
`dotnet run --project tests/Pacevite.Api.Tests -- --treenode-filter "/*/*/*/*[Category=Unit]"`

Integration only:
`dotnet run --project tests/Pacevite.Api.Tests -- --treenode-filter "/*/*/*/*[Category=Integration]"`

All .NET:
`dotnet run --project tests/Pacevite.Api.Tests`

## Frontend Tests

Vitest unit tests:
`cd src/Pacevite.Web && npm test`

Playwright E2E (auto-starts API + frontend if not running):
`cd src/Pacevite.Web && npm run test:e2e`

## Defaults

If no argument is given, run unit tests only (fastest feedback loop):
`dotnet run --project tests/Pacevite.Api.Tests -- --treenode-filter "/*/*/*/*[Category=Unit]"`

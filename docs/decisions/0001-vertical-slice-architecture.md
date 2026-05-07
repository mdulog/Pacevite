# 0001 — Use Vertical Slice Architecture

**Status:** Accepted

## Context and Problem Statement

The API needs a code organisation strategy. The two dominant options for an ASP.NET Core project are layered architecture (Controllers → Services → Repositories) and vertical slice architecture (one folder per feature, owning all its code).

## Considered Options

1. **Layered architecture** — horizontal layers (Controller, Service, Repository) shared across all features
2. **Vertical slice architecture** — one folder per feature, each owning its command/query, handler, validator, and endpoint registration

## Decision Outcome

Chose **vertical slice architecture** under `Features/{FeatureName}/{Operation}/`.

Each feature folder owns its `ICommand`/`IQuery` record, `ICommandHandler`/`IQueryHandler`, `AbstractValidator`, and endpoint registration. Nothing crosses feature boundaries except shared contracts in `Contracts/` and domain entities in `Domain/`.

**Why:**
- Eliminates the horizontal coupling of layered architecture — a change to the `Upload` feature touches only `Features/Events/Upload/`, never a shared `EventService` that other features also depend on.
- Feature folders are the natural unit of review, testing, and deletion.
- Fits well with the source-generated Mediator, which enforces a single handler per message type at compile time.

## Consequences

- **Easier:** Adding a new feature, reviewing a feature, deleting a feature.
- **Harder:** Finding logic that genuinely spans multiple features — it must go in `Infrastructure/` or `Domain/`, never in a feature folder.
- Handlers must not call other handlers via `IMediator` — that would reintroduce coupling. Cross-feature logic goes into a shared domain service in `Infrastructure/`.

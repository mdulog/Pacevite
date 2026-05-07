# 0002 — Use Source-Generated Mediator over MediatR

**Status:** Accepted

## Context and Problem Statement

The vertical slice architecture requires a mediator to decouple endpoints from handlers. The two mainstream options for .NET are MediatR (reflection-based) and martinothamar/Mediator (source-generated).

## Considered Options

1. **MediatR** — widely used, reflection-based dispatch, runtime handler discovery
2. **martinothamar/Mediator** — source-generated dispatch, compile-time handler discovery, AOT-compatible

## Decision Outcome

Chose **martinothamar/Mediator** (`ICommandHandler<TCommand, TResponse>` / `IQueryHandler<TQuery, TResponse>`).

Handlers are discovered and wired at compile time via source generation. The dispatch path is a direct method call with zero reflection overhead.

**Why:**
- Zero overhead vs MediatR's reflection-based `Send()` — matters in a hot path (every request goes through the mediator).
- Compile-time safety: referencing a command type with no registered handler is a build error, not a runtime `InvalidOperationException`.
- AOT-compatible — required if the AOT support plan (`docs/plans/2026-04-11-aot-support.md`) is implemented.

## Consequences

- **Easier:** Performance, AOT publishing, catching missing handlers at compile time.
- **Harder:** Adding a new handler requires a build before the source-generated dispatcher recognises it — the IDE may show a red squiggle until the next build.
- `IPipelineBehavior` equivalent is `MessageHandler<,>` decorators — slightly different API than MediatR's pipeline.

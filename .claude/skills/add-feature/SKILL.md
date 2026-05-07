---
name: add-feature
description: Scaffold a complete vertical slice under src/Pacevite.Api/Features/ following the existing Command/Handler/Validator/Endpoint pattern.
---

Given a feature name and HTTP verb, create the following files mirroring the structure in `src/Pacevite.Api/Features/Events/`:

## Files to create

**Command or Query** (`Features/{Name}/{Verb}{Name}Command.cs` or `...Query.cs`):
- Record with all input fields
- `ICommand<TResponse>` for mutations, `IQuery<TResponse>` for reads (from Mediator)

**Handler** (`Features/{Name}/{Verb}{Name}Handler.cs`):
- `sealed class` implementing `ICommandHandler<TCommand, TResponse>` or `IQueryHandler<TQuery, TResponse>`
- Constructor-inject `AppDbContext`, logger, and any services needed
- Wrap body in try/catch — log at Critical on exception, re-throw

**Validator** (`Features/{Name}/{Verb}{Name}Validator.cs`):
- Extend `AbstractValidator<TCommand>`
- Add FluentValidation rules for all required fields

**Endpoints** (`Features/{Name}/{Name}Endpoints.cs`):
- Static class with `MapXxxEndpoints(this IEndpointRouteBuilder app)` extension
- Use `TypedResults` for all return types
- Extract `userId` from `ClaimsPrincipal` using `FindFirstValue(ClaimTypes.NameIdentifier)`

## Wiring

In `src/Pacevite.Api/Program.cs`:
1. Register the validator: `builder.Services.AddScoped<IValidator<TCommand>, TValidator>()`
2. Map the endpoint group: `app.MapGroup("/api/{name}").RequireAuthorization().MapXxxEndpoints()`

## Tests

Generate a TUnit integration test skeleton in `tests/Pacevite.Api.Tests/Integration/{Name}EndpointsTests.cs` following `AuthEndpointsTests.cs` — include `[Category("Integration")]`, `[Before(Test)]`/`[After(Test)]` with Testcontainers Postgres, and stubs for the happy path and a 401 unauthorized case.

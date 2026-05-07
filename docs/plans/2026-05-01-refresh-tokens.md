# Refresh Tokens Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add 7-day httpOnly cookie refresh tokens with silent rotation, server-side revocation on logout, and an Axios interceptor that transparently re-issues the access token on 401.

**Architecture:** New `RefreshToken` domain entity + standalone DB table; `IRefreshTokenService` in `Infrastructure/Auth/`; two new feature slices (`Refresh`, `Logout`); updated `Login`/`Register` handlers create refresh tokens; Axios response interceptor silently rotates access token on expiry, queueing concurrent requests.

**Tech Stack:** ASP.NET Core cookie API, `System.Security.Cryptography.RandomNumberGenerator` + SHA-256, Axios response interceptors, TUnit integration tests with Testcontainers `postgres:17`.

---

## File Map

| File | Action | Purpose |
|---|---|---|
| `Domain/Entities/RefreshToken.cs` | Create | Refresh token entity with `IsActive` helper |
| `Infrastructure/Auth/JwtTokenService.cs` | Modify | Add `GenerateRefreshToken()`, `HashToken()`, configurable expiry |
| `Infrastructure/Auth/RefreshTokenService.cs` | Create | `IRefreshTokenService` + implementation |
| `Infrastructure/Persistence/AppDbContext.cs` | Modify | Add `DbSet<RefreshToken>` + Fluent API config |
| `Migrations/` | Create | `AddRefreshTokens` migration via `/ef-migrate` |
| `Features/Auth/AuthResult.cs` | Modify | Add `RefreshToken` property + updated `Ok()` factory |
| `Features/Auth/Login/LoginHandler.cs` | Modify | Inject `IRefreshTokenService`, call `CreateAsync` |
| `Features/Auth/Register/RegisterHandler.cs` | Modify | Inject `IRefreshTokenService`, call `CreateAsync` |
| `Features/Auth/Refresh/RefreshCommand.cs` | Create | Command record + `RefreshResult` type |
| `Features/Auth/Refresh/RefreshHandler.cs` | Create | Rotates token, issues new JWT |
| `Features/Auth/Logout/LogoutCommand.cs` | Create | Command record |
| `Features/Auth/Logout/LogoutHandler.cs` | Create | Revokes token |
| `Features/Auth/AuthEndpoints.cs` | Modify | Add `/refresh` + `/logout` endpoints + cookie helpers |
| `Contracts/Responses/RefreshResponse.cs` | Create | `record RefreshResponse(string Token)` |
| `Program.cs` | Modify | Register `IRefreshTokenService` as scoped |
| `appsettings.json` | Modify | Add `Jwt:AccessTokenExpiryMinutes: 15` |
| `appsettings.Development.json` | Modify | Override to `60` for dev convenience |
| `tests/…/Unit/Auth/JwtTokenServiceTests.cs` | Create | Unit tests for `GenerateRefreshToken` + `HashToken` |
| `tests/…/Integration/Auth/RefreshTokenEndpointTests.cs` | Create | Integration tests for refresh + logout |
| `src/Pacevite.Web/src/lib/api.ts` | Modify | Add `setLogoutCallback` + response interceptor with queue |
| `src/Pacevite.Web/src/context/AuthContext.tsx` | Modify | Register callback, update `logout` to call server |

---

### Task 1: RefreshToken entity

**Files:**
- Create: `src/Pacevite.Api/Domain/Entities/RefreshToken.cs`

- [ ] Create the file:

```csharp
namespace Pacevite.Api.Domain.Entities;

public class RefreshToken
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string UserId { get; init; }
    public required string TokenHash { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    public bool IsActive => RevokedAt is null && !IsExpired;
}
```

- [ ] Commit:

```bash
git add src/Pacevite.Api/Domain/Entities/RefreshToken.cs
git commit -m "feat(auth): add RefreshToken domain entity"
```

---

### Task 2: JwtTokenService — GenerateRefreshToken, HashToken, configurable expiry

**Files:**
- Modify: `src/Pacevite.Api/Infrastructure/Auth/JwtTokenService.cs`
- Modify: `src/Pacevite.Api/appsettings.json`
- Modify: `src/Pacevite.Api/appsettings.Development.json`
- Create: `tests/Pacevite.Api.Tests/Unit/Auth/JwtTokenServiceTests.cs`

- [ ] Write the failing unit tests:

```csharp
using Microsoft.Extensions.Configuration;
using Pacevite.Api.Infrastructure.Auth;
using TUnit.Assertions;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Auth;

[Category("Unit")]
public sealed class JwtTokenServiceTests
{
    private IJwtTokenService _sut = null!;

    [Before(Test)]
    public void SetUp()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "super-secret-key-for-testing-only-32c",
                ["Jwt:Issuer"] = "pacevite-test",
                ["Jwt:Audience"] = "pacevite-test",
                ["Jwt:AccessTokenExpiryMinutes"] = "15"
            })
            .Build();
        _sut = new JwtTokenService(config);
    }

    [Test]
    public async Task generate_refresh_token_returns_64_byte_base64_string()
    {
        // Arrange + Act
        var token = _sut.GenerateRefreshToken();
        var bytes = Convert.FromBase64String(token);

        // Assert
        await Assert.That(bytes.Length).IsEqualTo(64);
    }

    [Test]
    public async Task generate_refresh_token_returns_unique_values()
    {
        // Arrange + Act
        var token1 = _sut.GenerateRefreshToken();
        var token2 = _sut.GenerateRefreshToken();

        // Assert
        await Assert.That(token1).IsNotEqualTo(token2);
    }

    [Test]
    public async Task hash_token_is_deterministic()
    {
        // Arrange
        var raw = _sut.GenerateRefreshToken();

        // Act
        var hash1 = _sut.HashToken(raw);
        var hash2 = _sut.HashToken(raw);

        // Assert
        await Assert.That(hash1).IsEqualTo(hash2);
    }

    [Test]
    public async Task hash_token_produces_different_hashes_for_different_inputs()
    {
        // Arrange + Act
        var hash1 = _sut.HashToken("token-a");
        var hash2 = _sut.HashToken("token-b");

        // Assert
        await Assert.That(hash1).IsNotEqualTo(hash2);
    }
}
```

- [ ] Run to confirm compile failure:

```bash
dotnet run --project tests/Pacevite.Api.Tests -- --treenode-filter "/*/*/*/*[Category=Unit]"
```

Expected: build error — `GenerateRefreshToken` and `HashToken` not defined on `IJwtTokenService`.

- [ ] Replace `src/Pacevite.Api/Infrastructure/Auth/JwtTokenService.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace Pacevite.Api.Infrastructure.Auth;

public interface IJwtTokenService
{
    string GenerateToken(IdentityUser user);
    string GenerateRefreshToken();
    string HashToken(string rawToken);
}

public class JwtTokenService(IConfiguration configuration) : IJwtTokenService
{
    private const int DefaultExpiryMinutes = 15;

    public string GenerateToken(IdentityUser user)
    {
        var secret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret is not configured.");

        var expiryMinutes = int.TryParse(configuration["Jwt:AccessTokenExpiryMinutes"], out var minutes)
            ? minutes
            : DefaultExpiryMinutes;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }

    public string HashToken(string rawToken)
    {
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }
}
```

- [ ] Add `Jwt:AccessTokenExpiryMinutes` to `src/Pacevite.Api/appsettings.json` inside the existing `Jwt` section:

```json
"Jwt:AccessTokenExpiryMinutes": 15
```

- [ ] Add development override to `src/Pacevite.Api/appsettings.Development.json` inside the existing `Jwt` section:

```json
"Jwt:AccessTokenExpiryMinutes": 60
```

(60 minutes in dev avoids constant re-auth while developing.)

- [ ] Run unit tests to confirm they pass:

```bash
dotnet run --project tests/Pacevite.Api.Tests -- --treenode-filter "/*/*/*/*[Category=Unit]"
```

Expected: `JwtTokenServiceTests` — 4 passing.

- [ ] Commit:

```bash
git add src/Pacevite.Api/Infrastructure/Auth/JwtTokenService.cs \
        src/Pacevite.Api/appsettings.json \
        src/Pacevite.Api/appsettings.Development.json \
        tests/Pacevite.Api.Tests/Unit/Auth/JwtTokenServiceTests.cs
git commit -m "feat(auth): add GenerateRefreshToken and HashToken, make access token expiry configurable"
```

---

### Task 3: AppDbContext — add RefreshTokens table

**Files:**
- Modify: `src/Pacevite.Api/Infrastructure/Persistence/AppDbContext.cs`

- [ ] Add the `DbSet` property and Fluent API configuration. Append the following inside `OnModelCreating`, after the `EventSplit` entity configuration:

```csharp
builder.Entity<RefreshToken>(entity =>
{
    entity.HasKey(rt => rt.Id);
    entity.HasIndex(rt => rt.TokenHash).IsUnique();
    entity.HasIndex(rt => new { rt.UserId, rt.RevokedAt });
    entity.Ignore(rt => rt.IsExpired);
    entity.Ignore(rt => rt.IsActive);
});
```

Add the `DbSet` alongside the existing ones:

```csharp
public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
```

Add the missing using at the top if not already present:

```csharp
using Pacevite.Api.Domain.Entities;
```

- [ ] Build to confirm no errors:

```bash
dotnet build src/Pacevite.Api --no-restore -q
```

- [ ] Commit:

```bash
git add src/Pacevite.Api/Infrastructure/Persistence/AppDbContext.cs
git commit -m "feat(auth): add RefreshTokens DbSet and table configuration to AppDbContext"
```

---

### Task 4: EF Core migration

- [ ] Run the migration skill:

```
/ef-migrate AddRefreshTokens
```

This runs the 3-step workflow: add migration, apply to dev DB, update snapshot.

- [ ] Verify the new migration file appears under `src/Pacevite.Api/Migrations/` and references `RefreshTokens`.

- [ ] Commit:

```bash
git add src/Pacevite.Api/Migrations/
git commit -m "feat(auth): add EF Core migration AddRefreshTokens"
```

---

### Task 5: IRefreshTokenService + implementation

**Files:**
- Create: `src/Pacevite.Api/Infrastructure/Auth/RefreshTokenService.cs`

- [ ] Create the file:

```csharp
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Infrastructure.Persistence;

namespace Pacevite.Api.Infrastructure.Auth;

public interface IRefreshTokenService
{
    Task<string> CreateAsync(string userId, CancellationToken ct = default);
    Task<(bool Valid, string? UserId, string? NewRawToken)> RotateAsync(string rawToken, CancellationToken ct = default);
    Task RevokeAsync(string rawToken, CancellationToken ct = default);
}

public class RefreshTokenService(
    AppDbContext db,
    IJwtTokenService jwtTokenService,
    ILogger<RefreshTokenService> logger) : IRefreshTokenService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(7);

    public async Task<string> CreateAsync(string userId, CancellationToken ct = default)
    {
        var rawToken = jwtTokenService.GenerateRefreshToken();
        var tokenHash = jwtTokenService.HashToken(rawToken);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.Add(TokenLifetime)
        });

        await db.SaveChangesAsync(ct);
        return rawToken;
    }

    public async Task<(bool Valid, string? UserId, string? NewRawToken)> RotateAsync(
        string rawToken,
        CancellationToken ct = default)
    {
        var tokenHash = jwtTokenService.HashToken(rawToken);
        var existing = await db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, ct);

        if (existing is null || !existing.IsActive)
        {
            logger.LogWarning("Refresh token rotation rejected: token not found or inactive");
            return (false, null, null);
        }

        var newRawToken = jwtTokenService.GenerateRefreshToken();
        var newTokenHash = jwtTokenService.HashToken(newRawToken);

        existing.RevokedAt = DateTimeOffset.UtcNow;
        existing.ReplacedByTokenHash = newTokenHash;

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = existing.UserId,
            TokenHash = newTokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.Add(TokenLifetime)
        });

        await db.SaveChangesAsync(ct);
        return (true, existing.UserId, newRawToken);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken ct = default)
    {
        var tokenHash = jwtTokenService.HashToken(rawToken);
        var existing = await db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash && rt.RevokedAt == null, ct);

        if (existing is null)
            return;

        existing.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] Build to confirm no errors:

```bash
dotnet build src/Pacevite.Api --no-restore -q
```

- [ ] Commit:

```bash
git add src/Pacevite.Api/Infrastructure/Auth/RefreshTokenService.cs
git commit -m "feat(auth): add IRefreshTokenService + RefreshTokenService"
```

---

### Task 6: Update AuthResult — add RefreshToken property

**Files:**
- Modify: `src/Pacevite.Api/Features/Auth/AuthResult.cs`

- [ ] Replace the file:

```csharp
namespace Pacevite.Api.Features.Auth;

public sealed class AuthResult
{
    public bool IsSuccess { get; private init; }
    public bool IsDuplicate { get; private init; }
    public string? UserId { get; private init; }
    public string? Email { get; private init; }
    public string? Token { get; private init; }
    public string? RefreshToken { get; private init; }
    public string? Error { get; private init; }

    private AuthResult() { }

    public static AuthResult Ok(string userId, string email, string token, string refreshToken) => new()
    {
        IsSuccess = true,
        UserId = userId,
        Email = email,
        Token = token,
        RefreshToken = refreshToken
    };

    public static AuthResult Fail(string error) => new()
    {
        IsSuccess = false,
        Error = error
    };

    public static AuthResult FailDuplicate(string error) => new()
    {
        IsSuccess = false,
        IsDuplicate = true,
        Error = error
    };
}
```

- [ ] Build — expect compile errors in `LoginHandler` and `RegisterHandler` since `Ok()` now requires 4 args. These are fixed in the next task.

```bash
dotnet build src/Pacevite.Api --no-restore -q
```

Expected: 2 errors referencing `AuthResult.Ok`.

- [ ] Commit:

```bash
git add src/Pacevite.Api/Features/Auth/AuthResult.cs
git commit -m "feat(auth): add RefreshToken property to AuthResult"
```

---

### Task 7: Update Login + Register handlers

**Files:**
- Modify: `src/Pacevite.Api/Features/Auth/Login/LoginHandler.cs`
- Modify: `src/Pacevite.Api/Features/Auth/Register/RegisterHandler.cs`

- [ ] Replace `LoginHandler.cs`:

```csharp
using Mediator;
using Microsoft.AspNetCore.Identity;
using Pacevite.Api.Infrastructure.Auth;

namespace Pacevite.Api.Features.Auth.Login;

public sealed class LoginHandler(
    UserManager<IdentityUser> userManager,
    IJwtTokenService jwtTokenService,
    IRefreshTokenService refreshTokenService,
    ILogger<LoginHandler> logger) : ICommandHandler<LoginCommand, AuthResult>
{
    public async ValueTask<AuthResult> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(command.Email);

        // Do not reveal whether the email exists — OWASP A07
        if (user is null || !await userManager.CheckPasswordAsync(user, command.Password))
        {
            if (user is not null)
                logger.LogWarning("Failed login attempt for {UserId}", user.Id);

            return AuthResult.Fail("Invalid credentials.");
        }

        logger.LogInformation("User logged in: {UserId}", user.Id);
        var token = jwtTokenService.GenerateToken(user);
        var rawRefreshToken = await refreshTokenService.CreateAsync(user.Id, cancellationToken);
        return AuthResult.Ok(user.Id, user.Email!, token, rawRefreshToken);
    }
}
```

- [ ] Replace `RegisterHandler.cs`:

```csharp
using Mediator;
using Microsoft.AspNetCore.Identity;
using Pacevite.Api.Infrastructure.Auth;

namespace Pacevite.Api.Features.Auth.Register;

public sealed class RegisterHandler(
    UserManager<IdentityUser> userManager,
    IJwtTokenService jwtTokenService,
    IRefreshTokenService refreshTokenService,
    ILogger<RegisterHandler> logger) : ICommandHandler<RegisterCommand, AuthResult>
{
    public async ValueTask<AuthResult> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        var existing = await userManager.FindByEmailAsync(command.Email);
        if (existing is not null)
            return AuthResult.FailDuplicate("Email is already registered.");

        var user = new IdentityUser { UserName = command.Email, Email = command.Email };
        var result = await userManager.CreateAsync(user, command.Password);

        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            logger.LogWarning("Registration failed for email hash {EmailHash}: {Errors}",
                command.Email.GetHashCode(), errors);
            return AuthResult.Fail(errors);
        }

        logger.LogInformation("User registered: {UserId}", user.Id);
        var token = jwtTokenService.GenerateToken(user);
        var rawRefreshToken = await refreshTokenService.CreateAsync(user.Id, cancellationToken);
        return AuthResult.Ok(user.Id, user.Email!, token, rawRefreshToken);
    }
}
```

- [ ] Build to confirm no errors:

```bash
dotnet build src/Pacevite.Api --no-restore -q
```

Expected: clean build.

- [ ] Commit:

```bash
git add src/Pacevite.Api/Features/Auth/Login/LoginHandler.cs \
        src/Pacevite.Api/Features/Auth/Register/RegisterHandler.cs
git commit -m "feat(auth): update Login and Register handlers to issue refresh tokens"
```

---

### Task 8: Refresh endpoint

**Files:**
- Create: `src/Pacevite.Api/Contracts/Responses/RefreshResponse.cs`
- Create: `src/Pacevite.Api/Features/Auth/Refresh/RefreshCommand.cs`
- Create: `src/Pacevite.Api/Features/Auth/Refresh/RefreshHandler.cs`

- [ ] Create `src/Pacevite.Api/Contracts/Responses/RefreshResponse.cs`:

```csharp
namespace Pacevite.Api.Contracts.Responses;

public sealed record RefreshResponse(string Token);
```

- [ ] Create `src/Pacevite.Api/Features/Auth/Refresh/RefreshCommand.cs`:

```csharp
using Mediator;

namespace Pacevite.Api.Features.Auth.Refresh;

public sealed record RefreshCommand(string? RawToken) : ICommand<RefreshResult>;

public sealed class RefreshResult
{
    public bool IsSuccess { get; private init; }
    public string? Token { get; private init; }
    public string? NewRefreshToken { get; private init; }

    private RefreshResult() { }

    public static RefreshResult Ok(string token, string newRefreshToken) => new()
    {
        IsSuccess = true,
        Token = token,
        NewRefreshToken = newRefreshToken
    };

    public static RefreshResult Fail() => new() { IsSuccess = false };
}
```

- [ ] Create `src/Pacevite.Api/Features/Auth/Refresh/RefreshHandler.cs`:

```csharp
using Mediator;
using Microsoft.AspNetCore.Identity;
using Pacevite.Api.Infrastructure.Auth;

namespace Pacevite.Api.Features.Auth.Refresh;

public sealed class RefreshHandler(
    UserManager<IdentityUser> userManager,
    IJwtTokenService jwtTokenService,
    IRefreshTokenService refreshTokenService,
    ILogger<RefreshHandler> logger) : ICommandHandler<RefreshCommand, RefreshResult>
{
    public async ValueTask<RefreshResult> Handle(RefreshCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RawToken))
            return RefreshResult.Fail();

        var (valid, userId, newRawToken) = await refreshTokenService.RotateAsync(command.RawToken, cancellationToken);

        if (!valid || userId is null || newRawToken is null)
            return RefreshResult.Fail();

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            logger.LogWarning("Refresh token valid but user {UserId} not found", userId);
            return RefreshResult.Fail();
        }

        return RefreshResult.Ok(jwtTokenService.GenerateToken(user), newRawToken);
    }
}
```

- [ ] Build to confirm no errors:

```bash
dotnet build src/Pacevite.Api --no-restore -q
```

- [ ] Commit:

```bash
git add src/Pacevite.Api/Contracts/Responses/RefreshResponse.cs \
        src/Pacevite.Api/Features/Auth/Refresh/
git commit -m "feat(auth): add Refresh command, handler, and response contract"
```

---

### Task 9: Logout endpoint

**Files:**
- Create: `src/Pacevite.Api/Features/Auth/Logout/LogoutCommand.cs`
- Create: `src/Pacevite.Api/Features/Auth/Logout/LogoutHandler.cs`

- [ ] Create `src/Pacevite.Api/Features/Auth/Logout/LogoutCommand.cs`:

```csharp
using Mediator;

namespace Pacevite.Api.Features.Auth.Logout;

public sealed record LogoutCommand(string? RawToken) : ICommand<bool>;
```

- [ ] Create `src/Pacevite.Api/Features/Auth/Logout/LogoutHandler.cs`:

```csharp
using Mediator;
using Pacevite.Api.Infrastructure.Auth;

namespace Pacevite.Api.Features.Auth.Logout;

public sealed class LogoutHandler(
    IRefreshTokenService refreshTokenService,
    ILogger<LogoutHandler> logger) : ICommandHandler<LogoutCommand, bool>
{
    public async ValueTask<bool> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(command.RawToken))
        {
            await refreshTokenService.RevokeAsync(command.RawToken, cancellationToken);
            logger.LogInformation("Refresh token revoked on logout");
        }

        return true;
    }
}
```

- [ ] Build to confirm no errors:

```bash
dotnet build src/Pacevite.Api --no-restore -q
```

- [ ] Commit:

```bash
git add src/Pacevite.Api/Features/Auth/Logout/
git commit -m "feat(auth): add Logout command and handler"
```

---

### Task 10: Wire endpoints + cookie helpers in AuthEndpoints

**Files:**
- Modify: `src/Pacevite.Api/Features/Auth/AuthEndpoints.cs`

- [ ] Replace `AuthEndpoints.cs`:

```csharp
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Pacevite.Api.Contracts.Requests;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Features.Auth.Login;
using Pacevite.Api.Features.Auth.Logout;
using Pacevite.Api.Features.Auth.Refresh;
using Pacevite.Api.Features.Auth.Register;

namespace Pacevite.Api.Features.Auth;

public static class AuthEndpoints
{
    private const string RefreshTokenCookie = "refreshToken";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth")
            .RequireRateLimiting("auth")
            .AllowAnonymous();

        group.MapPost("/register", RegisterAsync).WithName("Register");
        group.MapPost("/login", LoginAsync).WithName("Login");
        group.MapPost("/refresh", RefreshAsync).WithName("Refresh");
        group.MapPost("/logout", LogoutAsync).WithName("Logout").RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<AuthResponse>, ProblemHttpResult>> RegisterAsync(
        [FromBody] RegisterRequest request,
        IMediator mediator,
        HttpContext httpContext,
        IWebHostEnvironment env,
        CancellationToken ct)
    {
        var result = await mediator.Send(new RegisterCommand(request.Email, request.Password), ct);

        if (result.IsSuccess)
        {
            SetRefreshCookie(httpContext, result.RefreshToken!, env.IsProduction());
            return TypedResults.Created($"/api/users/{result.UserId}", new AuthResponse(result.UserId!, result.Email!, result.Token!));
        }

        var statusCode = result.IsDuplicate
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status400BadRequest;

        return TypedResults.Problem(result.Error, statusCode: statusCode);
    }

    private static async Task<Results<Ok<AuthResponse>, UnauthorizedHttpResult>> LoginAsync(
        [FromBody] LoginRequest request,
        IMediator mediator,
        HttpContext httpContext,
        IWebHostEnvironment env,
        CancellationToken ct)
    {
        var result = await mediator.Send(new LoginCommand(request.Email, request.Password), ct);

        if (result.IsSuccess)
        {
            SetRefreshCookie(httpContext, result.RefreshToken!, env.IsProduction());
            return TypedResults.Ok(new AuthResponse(result.UserId!, result.Email!, result.Token!));
        }

        return TypedResults.Unauthorized();
    }

    private static async Task<Results<Ok<RefreshResponse>, UnauthorizedHttpResult>> RefreshAsync(
        IMediator mediator,
        HttpContext httpContext,
        IWebHostEnvironment env,
        CancellationToken ct)
    {
        var rawToken = httpContext.Request.Cookies[RefreshTokenCookie];
        var result = await mediator.Send(new RefreshCommand(rawToken), ct);

        if (result.IsSuccess)
        {
            SetRefreshCookie(httpContext, result.NewRefreshToken!, env.IsProduction());
            return TypedResults.Ok(new RefreshResponse(result.Token!));
        }

        ClearRefreshCookie(httpContext);
        return TypedResults.Unauthorized();
    }

    private static async Task<NoContent> LogoutAsync(
        IMediator mediator,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var rawToken = httpContext.Request.Cookies[RefreshTokenCookie];
        await mediator.Send(new LogoutCommand(rawToken), ct);
        ClearRefreshCookie(httpContext);
        return TypedResults.NoContent();
    }

    private static void SetRefreshCookie(HttpContext httpContext, string rawToken, bool isProduction)
    {
        httpContext.Response.Cookies.Append(RefreshTokenCookie, rawToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = isProduction,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            MaxAge = TimeSpan.FromDays(7)
        });
    }

    private static void ClearRefreshCookie(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(RefreshTokenCookie, new CookieOptions { Path = "/api/auth" });
    }
}
```

- [ ] Build to confirm no errors:

```bash
dotnet build src/Pacevite.Api --no-restore -q
```

- [ ] Commit:

```bash
git add src/Pacevite.Api/Features/Auth/AuthEndpoints.cs
git commit -m "feat(auth): add Refresh and Logout endpoints with httpOnly cookie helpers"
```

---

### Task 11: Register IRefreshTokenService in Program.cs

**Files:**
- Modify: `src/Pacevite.Api/Program.cs`

- [ ] Find the existing `IJwtTokenService` registration (line ~108):

```csharp
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
```

Add immediately after it:

```csharp
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
```

- [ ] Build to confirm no errors:

```bash
dotnet build src/Pacevite.Api --no-restore -q
```

- [ ] Start the API to confirm it starts and auto-migrates:

```bash
dotnet run --project src/Pacevite.Api --launch-profile http
```

Expected: API starts, `RefreshTokens` table visible in the dev DB.

- [ ] Commit:

```bash
git add src/Pacevite.Api/Program.cs
git commit -m "feat(auth): register IRefreshTokenService in DI"
```

---

### Task 12: Integration tests — refresh + logout

**Files:**
- Create: `tests/Pacevite.Api.Tests/Integration/Auth/RefreshTokenEndpointTests.cs`

- [ ] Create the test file:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pacevite.Api.Contracts.Requests;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using TUnit.Assertions;
using TUnit.Core;

namespace Pacevite.Api.Tests.Integration.Auth;

[Category("Integration")]
public sealed class RefreshTokenEndpointTests
{
    private PostgreSqlContainer _postgres = null!;
    private HttpClient _client = null!;
    private WebApplicationFactory<Program> _factory = null!;

    [Before(Test)]
    public async Task SetUpAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17")
            .WithDatabase("pacevite_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor is not null) services.Remove(descriptor);
                services.AddDbContext<AppDbContext>(opts => opts.UseNpgsql(_postgres.GetConnectionString()));
            });
            host.UseSetting("Jwt:Secret", "super-secret-key-for-testing-only-32c");
            host.UseSetting("Jwt:Issuer", "pacevite-test");
            host.UseSetting("Jwt:Audience", "pacevite-test");
            host.UseSetting("Jwt:AccessTokenExpiryMinutes", "15");
        });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [After(Test)]
    public async Task TearDownAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task<(string AccessToken, string RawRefreshToken)> RegisterAndGetTokensAsync(string email)
    {
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "Password123!"));
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Password123!"));
        var auth = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        var cookieHeader = resp.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("refreshToken="));
        var rawRefreshToken = cookieHeader.Split(';')[0].Split('=', 2)[1];
        return (auth!.Token, rawRefreshToken);
    }

    [Test]
    public async Task login_sets_httponly_refresh_token_cookie()
    {
        // Arrange
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("cookie-login@example.com", "Password123!"));

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("cookie-login@example.com", "Password123!"));

        // Assert
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        var refreshCookie = cookies.FirstOrDefault(c => c.Contains("refreshToken="));
        await Assert.That(refreshCookie).IsNotNull();
        await Assert.That(refreshCookie).Contains("httponly", StringComparison.OrdinalIgnoreCase);
        await Assert.That(refreshCookie).Contains("path=/api/auth", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task register_sets_httponly_refresh_token_cookie()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("cookie-register@example.com", "Password123!"));

        // Assert
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        var refreshCookie = cookies.FirstOrDefault(c => c.Contains("refreshToken="));
        await Assert.That(refreshCookie).IsNotNull();
        await Assert.That(refreshCookie).Contains("httponly", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task refresh_returns_new_access_token_when_cookie_valid()
    {
        // Arrange
        var (_, rawRefreshToken) = await RegisterAndGetTokensAsync("refresh-valid@example.com");
        _client.DefaultRequestHeaders.Add("Cookie", $"refreshToken={rawRefreshToken}");

        // Act
        var response = await _client.PostAsync("/api/auth/refresh", null);
        var body = await response.Content.ReadFromJsonAsync<RefreshResponse>();

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body!.Token).IsNotNull();
        await Assert.That(body.Token).IsNotEmpty();
    }

    [Test]
    public async Task refresh_rotates_cookie_to_new_value()
    {
        // Arrange
        var (_, rawRefreshToken) = await RegisterAndGetTokensAsync("refresh-rotate@example.com");
        _client.DefaultRequestHeaders.Add("Cookie", $"refreshToken={rawRefreshToken}");

        // Act
        var response = await _client.PostAsync("/api/auth/refresh", null);

        // Assert
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        var newCookie = cookies.FirstOrDefault(c => c.Contains("refreshToken="));
        await Assert.That(newCookie).IsNotNull();
        await Assert.That(newCookie).DoesNotContain(rawRefreshToken);
    }

    [Test]
    public async Task refresh_returns_401_when_cookie_missing()
    {
        // Act
        var response = await _client.PostAsync("/api/auth/refresh", null);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task refresh_returns_401_when_token_already_rotated()
    {
        // Arrange — use the token once to rotate it
        var (_, rawRefreshToken) = await RegisterAndGetTokensAsync("refresh-reuse@example.com");
        _client.DefaultRequestHeaders.Add("Cookie", $"refreshToken={rawRefreshToken}");
        await _client.PostAsync("/api/auth/refresh", null);

        // Act — attempt to reuse the original (now revoked) token
        var response = await _client.PostAsync("/api/auth/refresh", null);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task logout_revokes_token_so_subsequent_refresh_returns_401()
    {
        // Arrange
        var (accessToken, rawRefreshToken) = await RegisterAndGetTokensAsync("logout-revoke@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        _client.DefaultRequestHeaders.Add("Cookie", $"refreshToken={rawRefreshToken}");

        // Act
        var logoutResponse = await _client.PostAsync("/api/auth/logout", null);

        // Assert logout succeeded
        await Assert.That(logoutResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        // Assert revoked token no longer works
        var refreshResponse = await _client.PostAsync("/api/auth/refresh", null);
        await Assert.That(refreshResponse.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task logout_returns_204_when_no_refresh_cookie()
    {
        // Arrange
        var (accessToken, _) = await RegisterAndGetTokensAsync("logout-nocookie@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // Act
        var response = await _client.PostAsync("/api/auth/logout", null);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task logout_requires_authorization()
    {
        // Act — no Bearer token
        var response = await _client.PostAsync("/api/auth/logout", null);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] Run integration tests:

```bash
dotnet run --project tests/Pacevite.Api.Tests -- --treenode-filter "/*/*/*/*[Category=Integration]"
```

Expected: all 8 new tests pass.

- [ ] Commit:

```bash
git add tests/Pacevite.Api.Tests/Integration/Auth/RefreshTokenEndpointTests.cs
git commit -m "test(auth): add integration tests for refresh and logout endpoints"
```

---

### Task 13: Frontend — api.ts silent refresh interceptor

**Files:**
- Modify: `src/Pacevite.Web/src/lib/api.ts`

- [ ] Replace `src/Pacevite.Web/src/lib/api.ts`:

```typescript
import axios from 'axios'

// JWT stored in memory — never localStorage/sessionStorage (XSS resistance, ADR 0004)
let accessToken: string | null = null

export const tokenStore = {
  set: (token: string) => { accessToken = token },
  clear: () => { accessToken = null },
  get: () => accessToken,
}

// Registered by AuthProvider on mount so the interceptor can clear auth state
// without a circular import to AuthContext.
let onLogoutCallback: (() => void) | null = null

export function setLogoutCallback(cb: () => void) {
  onLogoutCallback = cb
}

export const apiClient = axios.create({ baseURL: '/api' })

apiClient.interceptors.request.use(config => {
  if (accessToken) config.headers.Authorization = `Bearer ${accessToken}`
  return config
})

let isRefreshing = false
let refreshQueue: Array<{
  resolve: (token: string) => void
  reject: (err: unknown) => void
}> = []

apiClient.interceptors.response.use(
  response => response,
  async error => {
    const originalRequest = error.config

    // Not a 401, or already retried after a refresh — don't attempt again
    if (error.response?.status !== 401 || originalRequest._isRetry) {
      return Promise.reject(error)
    }

    // A refresh is already in flight — queue this request to retry when it resolves
    if (isRefreshing) {
      return new Promise<string>((resolve, reject) => {
        refreshQueue.push({ resolve, reject })
      }).then(newToken => {
        originalRequest.headers.Authorization = `Bearer ${newToken}`
        return apiClient(originalRequest)
      })
    }

    originalRequest._isRetry = true
    isRefreshing = true

    try {
      const { data } = await apiClient.post<{ token: string }>('/auth/refresh')
      tokenStore.set(data.token)
      refreshQueue.forEach(({ resolve }) => resolve(data.token))
      refreshQueue = []
      originalRequest.headers.Authorization = `Bearer ${data.token}`
      return apiClient(originalRequest)
    } catch (refreshError) {
      refreshQueue.forEach(({ reject }) => reject(refreshError))
      refreshQueue = []
      onLogoutCallback?.()
      return Promise.reject(refreshError)
    } finally {
      isRefreshing = false
    }
  }
)
```

- [ ] Verify TypeScript:

```bash
cd src/Pacevite.Web && npx tsc --noEmit
```

Expected: no errors.

- [ ] Commit:

```bash
git add src/Pacevite.Web/src/lib/api.ts
git commit -m "feat(auth): add silent refresh interceptor with concurrent-request queue to Axios client"
```

---

### Task 14: Frontend — AuthContext server-side logout + callback registration

**Files:**
- Modify: `src/Pacevite.Web/src/context/AuthContext.tsx`

- [ ] Replace `src/Pacevite.Web/src/context/AuthContext.tsx`:

```tsx
import { createContext, useEffect, useState, type ReactNode } from 'react'
import { apiClient, setLogoutCallback, tokenStore } from '@/lib/api'

interface AuthState {
  userId: string
  email: string
}

interface AuthContextValue {
  user: AuthState | null
  isAuthenticated: boolean
  login: (userId: string, email: string, token: string) => void
  logout: () => Promise<void>
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthState | null>(null)

  // Register the force-logout callback used by the Axios interceptor when
  // a silent refresh fails (expired or revoked refresh token).
  useEffect(() => {
    setLogoutCallback(() => {
      tokenStore.clear()
      setUser(null)
    })
  }, [])

  function login(userId: string, email: string, token: string) {
    tokenStore.set(token)
    setUser({ userId, email })
  }

  async function logout() {
    try {
      await apiClient.post('/auth/logout')
    } catch {
      // Network error or already-expired access token — clear client state regardless
    } finally {
      tokenStore.clear()
      setUser(null)
    }
  }

  return (
    <AuthContext.Provider value={{ user, isAuthenticated: user !== null, login, logout }}>
      {children}
    </AuthContext.Provider>
  )
}
```

- [ ] Verify TypeScript:

```bash
cd src/Pacevite.Web && npx tsc --noEmit
```

Expected: no errors.

- [ ] Start the dev stack and verify end-to-end:
  1. Log in → open DevTools → Application → Cookies → confirm `refreshToken` cookie is `HttpOnly`
  2. Log out → confirm cookie is cleared, redirected to `/login`
  3. Log in again → confirm session works normally

- [ ] Commit:

```bash
git add src/Pacevite.Web/src/context/AuthContext.tsx
git commit -m "feat(auth): register force-logout callback and call server-side logout on sign out"
```

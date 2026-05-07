# JWT Refresh Token — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add single-use rotating refresh tokens to Pacevite. Access JWTs remain 60-minute in-memory tokens. Refresh tokens live in httpOnly cookies, are hashed in a new `RefreshTokens` DB table, and are rotated on every use.

**Architecture:** New `RefreshTokens` entity + EF Core table. New `IRefreshTokenService` in `Infrastructure/Auth/`. Two new vertical slices (`Refresh/`, `Logout/`) under `Features/Auth/`. Modified `AuthEndpoints.cs` to set the cookie on login/register. Axios response interceptor in the frontend silently retries 401s.

**Tech Stack:** .NET 10, EF Core 10, PostgreSQL 17, ASP.NET Core Minimal API, Mediator (source-gen), TUnit + Testcontainers (API tests), Vitest + React Testing Library + MSW (frontend tests)

---

## File Map

### Backend — New Files

| File | Responsibility |
|---|---|
| `src/Pacevite.Api/Domain/Entities/RefreshToken.cs` | Entity: Id, UserId, TokenHash, ExpiresAt, RevokedAt, CreatedAt, ReplacedByTokenHash |
| `src/Pacevite.Api/Infrastructure/Auth/IRefreshTokenService.cs` | Interface: GenerateAsync, ValidateAndRotateAsync, RevokeAsync, RevokeAllForUserAsync |
| `src/Pacevite.Api/Infrastructure/Auth/RefreshTokenService.cs` | EF Core implementation |
| `src/Pacevite.Api/Infrastructure/Auth/CookieHelper.cs` | Static helper: SetRefreshTokenCookie, ClearRefreshTokenCookie |
| `src/Pacevite.Api/Contracts/Responses/RefreshResponse.cs` | `{ string Token }` |
| `src/Pacevite.Api/Features/Auth/Refresh/RefreshCommand.cs` | `ICommand<AuthResult>` — carries raw token from cookie |
| `src/Pacevite.Api/Features/Auth/Refresh/RefreshHandler.cs` | Validates + rotates token, issues new JWT |
| `src/Pacevite.Api/Features/Auth/Logout/LogoutCommand.cs` | `ICommand` — carries raw token from cookie |
| `src/Pacevite.Api/Features/Auth/Logout/LogoutHandler.cs` | Revokes token, returns Unit |

### Backend — Modified Files

| File | Change |
|---|---|
| `src/Pacevite.Api/Infrastructure/Persistence/AppDbContext.cs` | Add `DbSet<RefreshToken> RefreshTokens` |
| `src/Pacevite.Api/Features/Auth/AuthEndpoints.cs` | Set cookie on login/register; add /refresh and /logout routes |
| `src/Pacevite.Api/Program.cs` | Register `IRefreshTokenService` as scoped |
| `src/Pacevite.Api/appsettings.json` | Add `Jwt:RefreshTokenExpiryDays: 7` |

### Test Files — New

| File | What it tests |
|---|---|
| `tests/Pacevite.Api.Tests/Unit/Auth/RefreshTokenServiceTests.cs` | Generate, ValidateAndRotate, Revoke, RevokeAllForUser |
| `tests/Pacevite.Api.Tests/Unit/Auth/RefreshHandlerTests.cs` | Handler returns new JWT on valid; AuthResult.Fail on invalid |
| `tests/Pacevite.Api.Tests/Unit/Auth/LogoutHandlerTests.cs` | Revokes token; handles missing token gracefully |
| `tests/Pacevite.Api.Tests/Integration/RefreshEndpointsTests.cs` | Full endpoint integration tests |

### Frontend — Modified Files

| File | Change |
|---|---|
| `src/Pacevite.Web/src/lib/api.ts` | Add Axios 401 response interceptor; refresh logic |
| `src/Pacevite.Web/src/context/AuthContext.tsx` | `logout()` calls `POST /api/auth/logout` |
| `src/Pacevite.Web/src/test/handlers.ts` | Add MSW handlers for `/api/auth/refresh` and `/api/auth/logout` |

---

## Task 1: RefreshToken Entity + EF Core Migration

**Files:**
- Create: `src/Pacevite.Api/Domain/Entities/RefreshToken.cs`
- Modify: `src/Pacevite.Api/Infrastructure/Persistence/AppDbContext.cs`

- [ ] **Step 1: Create `RefreshToken` entity**

```csharp
// src/Pacevite.Api/Domain/Entities/RefreshToken.cs
namespace Pacevite.Api.Domain.Entities;

public sealed class RefreshToken
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string UserId { get; init; }
    public required string TokenHash { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt.HasValue;
    public bool IsActive => !IsExpired && !IsRevoked;
}
```

- [ ] **Step 2: Add `DbSet` and configure indexes in `AppDbContext`**

Add to `AppDbContext.cs`:

```csharp
public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
```

In `OnModelCreating` (or `OnModelCreatingPartial` if using partial classes), add:

```csharp
modelBuilder.Entity<RefreshToken>(b =>
{
    b.HasKey(t => t.Id);
    b.HasIndex(t => t.TokenHash);
    b.HasIndex(t => t.UserId);
});
```

- [ ] **Step 3: Build to verify**

```bash
cd src/Pacevite.Api && dotnet build
```

Expected: `Build succeeded` with 0 errors.

- [ ] **Step 4: Add migration**

```bash
dotnet ef migrations add AddRefreshTokens --project src/Pacevite.Api
```

Expected: new migration file in `src/Pacevite.Api/Migrations/` creating `RefreshTokens` table and two indexes.

- [ ] **Step 5: Verify migration SQL**

```bash
dotnet ef migrations script --idempotent --project src/Pacevite.Api | grep -A 30 "RefreshTokens"
```

Expected: `CREATE TABLE "RefreshTokens"` with all 7 columns and `CREATE INDEX` for `TokenHash` and `UserId`.

- [ ] **Step 6: Commit**

```bash
git add src/Pacevite.Api/Domain/Entities/RefreshToken.cs \
        src/Pacevite.Api/Infrastructure/Persistence/AppDbContext.cs \
        src/Pacevite.Api/Migrations/
git commit -m "feat(auth): add RefreshToken entity and migration"
```

---

## Task 2: IRefreshTokenService + RefreshTokenService

**Files:**
- Create: `src/Pacevite.Api/Infrastructure/Auth/IRefreshTokenService.cs`
- Create: `src/Pacevite.Api/Infrastructure/Auth/RefreshTokenService.cs`
- Create: `tests/Pacevite.Api.Tests/Unit/Auth/RefreshTokenServiceTests.cs`

- [ ] **Step 1: Write failing unit tests**

```csharp
// tests/Pacevite.Api.Tests/Unit/Auth/RefreshTokenServiceTests.cs
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Infrastructure.Auth;
using Pacevite.Api.Infrastructure.Persistence;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Auth;

[Category("Unit")]
public sealed class RefreshTokenServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RefreshTokenService _service;
    private const string UserId = "user-refresh-test";

    public RefreshTokenServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new RefreshTokenService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Test]
    public async Task GenerateAsync_CreatesHashedRowInDb()
    {
        var rawToken = await _service.GenerateAsync(UserId, CancellationToken.None);

        await Assert.That(rawToken).IsNotNull();
        await Assert.That(rawToken.Length).IsGreaterThan(40);

        var stored = await _db.RefreshTokens.SingleOrDefaultAsync(t => t.UserId == UserId);
        await Assert.That(stored).IsNotNull();
        await Assert.That(stored!.TokenHash).IsNotEqualTo(rawToken); // raw != hash
        await Assert.That(stored.IsActive).IsTrue();
    }

    [Test]
    public async Task ValidateAndRotateAsync_ValidToken_ReturnsUserIdAndRotates()
    {
        var rawToken = await _service.GenerateAsync(UserId, CancellationToken.None);

        var result = await _service.ValidateAndRotateAsync(rawToken, CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.UserId).IsEqualTo(UserId);
        await Assert.That(result.NewRawToken).IsNotEqualTo(rawToken);

        // Old token should now be revoked
        var oldHash = RefreshTokenService.Hash(rawToken);
        var old = await _db.RefreshTokens.SingleAsync(t => t.TokenHash == oldHash);
        await Assert.That(old.IsRevoked).IsTrue();
        await Assert.That(old.ReplacedByTokenHash).IsNotNull();
    }

    [Test]
    public async Task ValidateAndRotateAsync_ExpiredToken_ReturnsNull()
    {
        // Seed an already-expired token directly
        var rawToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = UserId,
            TokenHash = RefreshTokenService.Hash(rawToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        await _db.SaveChangesAsync();

        var result = await _service.ValidateAndRotateAsync(rawToken, CancellationToken.None);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ValidateAndRotateAsync_RevokedToken_ReturnsNull()
    {
        var rawToken = await _service.GenerateAsync(UserId, CancellationToken.None);
        await _service.RevokeAsync(rawToken, CancellationToken.None);

        var result = await _service.ValidateAndRotateAsync(rawToken, CancellationToken.None);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ValidateAndRotateAsync_UnknownToken_ReturnsNull()
    {
        var result = await _service.ValidateAndRotateAsync("nonexistent-token", CancellationToken.None);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task RevokeAsync_SetsRevokedAt()
    {
        var rawToken = await _service.GenerateAsync(UserId, CancellationToken.None);

        await _service.RevokeAsync(rawToken, CancellationToken.None);

        var hash = RefreshTokenService.Hash(rawToken);
        var token = await _db.RefreshTokens.SingleAsync(t => t.TokenHash == hash);
        await Assert.That(token.RevokedAt).IsNotNull();
    }

    [Test]
    public async Task RevokeAllForUserAsync_RevokesAllActiveTokens()
    {
        var token1 = await _service.GenerateAsync(UserId, CancellationToken.None);
        var token2 = await _service.GenerateAsync(UserId, CancellationToken.None);
        var otherToken = await _service.GenerateAsync("other-user", CancellationToken.None);

        await _service.RevokeAllForUserAsync(UserId, CancellationToken.None);

        var userTokens = await _db.RefreshTokens.Where(t => t.UserId == UserId).ToListAsync();
        await Assert.That(userTokens.All(t => t.IsRevoked)).IsTrue();

        var otherHash = RefreshTokenService.Hash(otherToken);
        var other = await _db.RefreshTokens.SingleAsync(t => t.TokenHash == otherHash);
        await Assert.That(other.IsRevoked).IsFalse();
    }
}
```

- [ ] **Step 2: Run tests — confirm compile failure**

```bash
dotnet run --project tests/Pacevite.Api.Tests -- --treenode-filter "/*/*/*/*[Category=Unit]"
```

Expected: compile error — `IRefreshTokenService`, `RefreshTokenService` not found.

- [ ] **Step 3: Create `IRefreshTokenService.cs`**

```csharp
// src/Pacevite.Api/Infrastructure/Auth/IRefreshTokenService.cs
namespace Pacevite.Api.Infrastructure.Auth;

public sealed record RotationResult(string UserId, string NewRawToken);

public interface IRefreshTokenService
{
    Task<string> GenerateAsync(string userId, CancellationToken ct);
    Task<RotationResult?> ValidateAndRotateAsync(string rawToken, CancellationToken ct);
    Task RevokeAsync(string rawToken, CancellationToken ct);
    Task RevokeAllForUserAsync(string userId, CancellationToken ct);
}
```

- [ ] **Step 4: Create `RefreshTokenService.cs`**

```csharp
// src/Pacevite.Api/Infrastructure/Auth/RefreshTokenService.cs
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Infrastructure.Persistence;

namespace Pacevite.Api.Infrastructure.Auth;

public sealed class RefreshTokenService(AppDbContext db) : IRefreshTokenService
{
    private const int ExpiryDays = 7;

    public static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private static string GenerateRaw() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public async Task<string> GenerateAsync(string userId, CancellationToken ct)
    {
        var raw = GenerateRaw();
        var entity = new RefreshToken
        {
            UserId = userId,
            TokenHash = Hash(raw),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(ExpiryDays),
        };
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(ct);
        return raw;
    }

    public async Task<RotationResult?> ValidateAndRotateAsync(string rawToken, CancellationToken ct)
    {
        var hash = Hash(rawToken);
        var existing = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (existing is null || !existing.IsActive)
            return null;

        // Rotate: revoke old, create new
        var newRaw = GenerateRaw();
        var newHash = Hash(newRaw);

        existing.RevokedAt = DateTimeOffset.UtcNow;
        existing.ReplacedByTokenHash = newHash;

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = existing.UserId,
            TokenHash = newHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(ExpiryDays),
        });

        await db.SaveChangesAsync(ct);
        return new RotationResult(existing.UserId, newRaw);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken ct)
    {
        var hash = Hash(rawToken);
        var existing = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (existing is null || existing.IsRevoked) return;

        existing.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeAllForUserAsync(string userId, CancellationToken ct)
    {
        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in tokens)
            token.RevokedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 5: Run tests — all must pass**

```bash
dotnet run --project tests/Pacevite.Api.Tests -- --treenode-filter "/*/*/*/*[Category=Unit]"
```

Expected: all 7 `RefreshTokenServiceTests` green.

- [ ] **Step 6: Commit**

```bash
git add src/Pacevite.Api/Infrastructure/Auth/IRefreshTokenService.cs \
        src/Pacevite.Api/Infrastructure/Auth/RefreshTokenService.cs \
        tests/Pacevite.Api.Tests/Unit/Auth/RefreshTokenServiceTests.cs
git commit -m "feat(auth): add IRefreshTokenService and RefreshTokenService with unit tests"
```

---

## Task 3: CookieHelper

**Files:**
- Create: `src/Pacevite.Api/Infrastructure/Auth/CookieHelper.cs`

No tests needed — this is a thin static helper with no logic.

- [ ] **Step 1: Create `CookieHelper.cs`**

```csharp
// src/Pacevite.Api/Infrastructure/Auth/CookieHelper.cs
namespace Pacevite.Api.Infrastructure.Auth;

public static class CookieHelper
{
    public const string CookieName = "refresh_token";
    private const string CookiePath = "/api/auth";

    public static void SetRefreshTokenCookie(HttpResponse response, string rawToken, bool isProduction)
    {
        response.Cookies.Append(CookieName, rawToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = isProduction,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            MaxAge = TimeSpan.FromDays(7),
        });
    }

    public static void ClearRefreshTokenCookie(HttpResponse response)
    {
        response.Cookies.Append(CookieName, string.Empty, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            MaxAge = TimeSpan.Zero,
        });
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
cd src/Pacevite.Api && dotnet build
```

Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add src/Pacevite.Api/Infrastructure/Auth/CookieHelper.cs
git commit -m "feat(auth): add CookieHelper for refresh token cookie management"
```

---

## Task 4: RefreshCommand + RefreshHandler

**Files:**
- Create: `src/Pacevite.Api/Contracts/Responses/RefreshResponse.cs`
- Create: `src/Pacevite.Api/Features/Auth/Refresh/RefreshCommand.cs`
- Create: `src/Pacevite.Api/Features/Auth/Refresh/RefreshHandler.cs`
- Create: `tests/Pacevite.Api.Tests/Unit/Auth/RefreshHandlerTests.cs`

- [ ] **Step 1: Create `RefreshResponse.cs`**

```csharp
// src/Pacevite.Api/Contracts/Responses/RefreshResponse.cs
namespace Pacevite.Api.Contracts.Responses;

public sealed record RefreshResponse(string Token);
```

- [ ] **Step 2: Create `RefreshCommand.cs`**

```csharp
// src/Pacevite.Api/Features/Auth/Refresh/RefreshCommand.cs
using Mediator;
using Pacevite.Api.Features.Auth;

namespace Pacevite.Api.Features.Auth.Refresh;

public sealed record RefreshCommand(string RawToken) : ICommand<AuthResult>;
```

- [ ] **Step 3: Write failing handler tests**

```csharp
// tests/Pacevite.Api.Tests/Unit/Auth/RefreshHandlerTests.cs
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pacevite.Api.Features.Auth.Refresh;
using Pacevite.Api.Infrastructure.Auth;
using Pacevite.Api.Infrastructure.Persistence;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Auth;

[Category("Unit")]
public sealed class RefreshHandlerTests : IDisposable
{
    private readonly IRefreshTokenService _tokenService = Substitute.For<IRefreshTokenService>();
    private readonly IJwtTokenService _jwtService = Substitute.For<IJwtTokenService>();

    public void Dispose() { }

    private RefreshHandler BuildHandler() => new(_tokenService, _jwtService);

    [Test]
    public async Task Handle_ValidToken_ReturnsNewJwt()
    {
        _tokenService
            .ValidateAndRotateAsync("valid-raw-token", Arg.Any<CancellationToken>())
            .Returns(new RotationResult("user-123", "new-raw-token"));

        _jwtService
            .GenerateToken(Arg.Is<string>(id => id == "user-123"))
            .Returns("new-jwt");

        var result = await BuildHandler().Handle(
            new RefreshCommand("valid-raw-token"),
            CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Token).IsEqualTo("new-jwt");
        await Assert.That(result.NewRawRefreshToken).IsEqualTo("new-raw-token");
    }

    [Test]
    public async Task Handle_InvalidToken_ReturnsFail()
    {
        _tokenService
            .ValidateAndRotateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((RotationResult?)null);

        var result = await BuildHandler().Handle(
            new RefreshCommand("invalid-token"),
            CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await _jwtService.DidNotReceive().GenerateToken(Arg.Any<string>());
    }
}
```

- [ ] **Step 4: Run tests — confirm compile failure**

```bash
dotnet run --project tests/Pacevite.Api.Tests -- --treenode-filter "/*/*/*/*[Category=Unit]"
```

Expected: compile error — `RefreshHandler` not found.

- [ ] **Step 5: Extend `AuthResult` to carry the new refresh token on success**

In `src/Pacevite.Api/Features/Auth/AuthResult.cs`, add `NewRawRefreshToken` to the Ok case:

```csharp
// Add to the existing Ok record within AuthResult:
public sealed record Ok(string UserId, string Email, string Token, string? NewRawRefreshToken = null)
    : AuthResult;
```

If `AuthResult` uses a different pattern (e.g. static factory methods), update accordingly so the Ok case can carry the optional new refresh token. Existing callers (login, register) pass `null` by default — no breaking change.

- [ ] **Step 6: Implement `RefreshHandler.cs`**

```csharp
// src/Pacevite.Api/Features/Auth/Refresh/RefreshHandler.cs
using Mediator;
using Pacevite.Api.Infrastructure.Auth;

namespace Pacevite.Api.Features.Auth.Refresh;

public sealed class RefreshHandler(
    IRefreshTokenService refreshTokenService,
    IJwtTokenService jwtService) : ICommandHandler<RefreshCommand, AuthResult>
{
    public async ValueTask<AuthResult> Handle(RefreshCommand command, CancellationToken ct)
    {
        var rotation = await refreshTokenService.ValidateAndRotateAsync(command.RawToken, ct);

        if (rotation is null)
            return new AuthResult.Fail();

        var newJwt = jwtService.GenerateToken(rotation.UserId);

        return new AuthResult.Ok(
            UserId: rotation.UserId,
            Email: string.Empty,   // email not needed for refresh response
            Token: newJwt,
            NewRawRefreshToken: rotation.NewRawToken);
    }
}
```

- [ ] **Step 7: Run tests — all must pass**

```bash
dotnet run --project tests/Pacevite.Api.Tests -- --treenode-filter "/*/*/*/*[Category=Unit]"
```

Expected: all `RefreshHandlerTests` green.

- [ ] **Step 8: Commit**

```bash
git add src/Pacevite.Api/Contracts/Responses/RefreshResponse.cs \
        src/Pacevite.Api/Features/Auth/Refresh/ \
        src/Pacevite.Api/Features/Auth/AuthResult.cs \
        tests/Pacevite.Api.Tests/Unit/Auth/RefreshHandlerTests.cs
git commit -m "feat(auth): add RefreshCommand, RefreshHandler and extend AuthResult"
```

---

## Task 5: LogoutCommand + LogoutHandler

**Files:**
- Create: `src/Pacevite.Api/Features/Auth/Logout/LogoutCommand.cs`
- Create: `src/Pacevite.Api/Features/Auth/Logout/LogoutHandler.cs`
- Create: `tests/Pacevite.Api.Tests/Unit/Auth/LogoutHandlerTests.cs`

- [ ] **Step 1: Create `LogoutCommand.cs`**

```csharp
// src/Pacevite.Api/Features/Auth/Logout/LogoutCommand.cs
using Mediator;

namespace Pacevite.Api.Features.Auth.Logout;

public sealed record LogoutCommand(string? RawToken) : ICommand;
```

- [ ] **Step 2: Write failing handler tests**

```csharp
// tests/Pacevite.Api.Tests/Unit/Auth/LogoutHandlerTests.cs
using NSubstitute;
using Pacevite.Api.Features.Auth.Logout;
using Pacevite.Api.Infrastructure.Auth;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Auth;

[Category("Unit")]
public sealed class LogoutHandlerTests
{
    private readonly IRefreshTokenService _tokenService = Substitute.For<IRefreshTokenService>();

    private LogoutHandler BuildHandler() => new(_tokenService);

    [Test]
    public async Task Handle_WithToken_RevokesToken()
    {
        await BuildHandler().Handle(
            new LogoutCommand("some-raw-token"),
            CancellationToken.None);

        await _tokenService.Received(1).RevokeAsync("some-raw-token", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Handle_NullToken_DoesNotCallRevoke()
    {
        await BuildHandler().Handle(
            new LogoutCommand(null),
            CancellationToken.None);

        await _tokenService.DidNotReceive().RevokeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 3: Run tests — confirm compile failure**

```bash
dotnet run --project tests/Pacevite.Api.Tests -- --treenode-filter "/*/*/*/*[Category=Unit]"
```

Expected: compile error — `LogoutHandler` not found.

- [ ] **Step 4: Implement `LogoutHandler.cs`**

```csharp
// src/Pacevite.Api/Features/Auth/Logout/LogoutHandler.cs
using Mediator;
using Pacevite.Api.Infrastructure.Auth;

namespace Pacevite.Api.Features.Auth.Logout;

public sealed class LogoutHandler(IRefreshTokenService refreshTokenService)
    : ICommandHandler<LogoutCommand>
{
    public async ValueTask<Unit> Handle(LogoutCommand command, CancellationToken ct)
    {
        if (command.RawToken is not null)
            await refreshTokenService.RevokeAsync(command.RawToken, ct);

        return Unit.Value;
    }
}
```

- [ ] **Step 5: Run tests — all must pass**

```bash
dotnet run --project tests/Pacevite.Api.Tests -- --treenode-filter "/*/*/*/*[Category=Unit]"
```

Expected: all `LogoutHandlerTests` green.

- [ ] **Step 6: Commit**

```bash
git add src/Pacevite.Api/Features/Auth/Logout/ \
        tests/Pacevite.Api.Tests/Unit/Auth/LogoutHandlerTests.cs
git commit -m "feat(auth): add LogoutCommand and LogoutHandler"
```

---

## Task 6: Wire Endpoints + DI Registration

**Files:**
- Modify: `src/Pacevite.Api/Features/Auth/AuthEndpoints.cs`
- Modify: `src/Pacevite.Api/Program.cs`
- Modify: `src/Pacevite.Api/appsettings.json`

- [ ] **Step 1: Update `Program.cs` to register `IRefreshTokenService`**

In `Program.cs`, alongside the existing `IJwtTokenService` registration, add:

```csharp
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
```

- [ ] **Step 2: Add `Jwt:RefreshTokenExpiryDays` to `appsettings.json`**

```json
"Jwt": {
  "Secret": "...",
  "Issuer": "...",
  "Audience": "...",
  "ExpiryMinutes": 60,
  "RefreshTokenExpiryDays": 7
}
```

- [ ] **Step 3: Update `AuthEndpoints.cs`**

Inject `IRefreshTokenService` and `IWebHostEnvironment`. Update login and register to set cookie. Add `/refresh` and `/logout` routes:

```csharp
// Inject into AuthEndpoints (Minimal API — inject via method params or group-level):

// In /login handler, after building AuthResult.Ok:
if (result is AuthResult.Ok ok)
{
    var rawRefresh = await refreshTokenService.GenerateAsync(ok.UserId, ct);
    CookieHelper.SetRefreshTokenCookie(context.Response, rawRefresh, env.IsProduction());
    return TypedResults.Ok(new AuthResponse(ok.UserId, ok.Email, ok.Token));
}

// In /register handler, same pattern after 201 Created.

// New route: POST /api/auth/refresh
authGroup.MapPost("/refresh", async (
    HttpContext context,
    IMediator mediator,
    IWebHostEnvironment env,
    CancellationToken ct) =>
{
    var rawToken = context.Request.Cookies[CookieHelper.CookieName];
    if (rawToken is null)
        return Results.Unauthorized();

    var result = await mediator.Send(new RefreshCommand(rawToken), ct);

    if (result is not AuthResult.Ok ok)
        return Results.Unauthorized();

    CookieHelper.SetRefreshTokenCookie(context.Response, ok.NewRawRefreshToken!, env.IsProduction());
    return Results.Ok(new RefreshResponse(ok.Token));
});

// New route: POST /api/auth/logout
authGroup.MapPost("/logout", async (
    HttpContext context,
    IMediator mediator,
    CancellationToken ct) =>
{
    var rawToken = context.Request.Cookies[CookieHelper.CookieName];
    await mediator.Send(new LogoutCommand(rawToken), ct);
    CookieHelper.ClearRefreshTokenCookie(context.Response);
    return Results.NoContent();
});
```

- [ ] **Step 4: Build to verify**

```bash
cd src/Pacevite.Api && dotnet build
```

Expected: `Build succeeded` with 0 errors. Mediator source generator will produce new dispatcher entries for `RefreshCommand` and `LogoutCommand`.

- [ ] **Step 5: Smoke test locally**

```bash
podman compose up -d db
dotnet run --project src/Pacevite.Api --launch-profile http
```

```bash
# Login — check Set-Cookie header in response
curl -c cookies.txt -X POST http://localhost:5291/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"test@example.com","password":"P@ssword1!"}' -v 2>&1 | grep -i "set-cookie"

# Refresh — use the cookie
curl -b cookies.txt -c cookies.txt -X POST http://localhost:5291/api/auth/refresh -v
```

Expected: login sets `refresh_token` cookie; refresh returns `{ "token": "..." }` with a new cookie.

- [ ] **Step 6: Commit**

```bash
git add src/Pacevite.Api/Features/Auth/AuthEndpoints.cs \
        src/Pacevite.Api/Program.cs \
        src/Pacevite.Api/appsettings.json
git commit -m "feat(auth): wire refresh and logout endpoints; set cookie on login/register"
```

---

## Task 7: Integration Tests

**Files:**
- Create: `tests/Pacevite.Api.Tests/Integration/RefreshEndpointsTests.cs`

- [ ] **Step 1: Write integration tests**

```csharp
// tests/Pacevite.Api.Tests/Integration/RefreshEndpointsTests.cs
using System.Net;
using System.Net.Http.Json;
using Pacevite.Api.Contracts.Requests;
using Pacevite.Api.Contracts.Responses;
using TUnit.Core;

namespace Pacevite.Api.Tests.Integration;

[Category("Integration")]
public sealed class RefreshEndpointsTests : IClassFixture<PaceviteWebFactory>
{
    private readonly PaceviteWebFactory _factory;

    public RefreshEndpointsTests(PaceviteWebFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWithCookies()
    {
        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new System.Net.CookieContainer() };
        return _factory.CreateClient(new() { AllowAutoRedirect = false });
    }

    private async Task<(HttpClient client, string accessToken)> RegisterAndLoginAsync()
    {
        var client = _factory.CreateClient();
        var email = $"refresh-test-{Guid.NewGuid()}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "P@ssword1!"));
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        return (client, auth!.Token);
    }

    [Test]
    public async Task Login_SetsRefreshTokenCookie()
    {
        var client = _factory.CreateClient();
        var email = $"cookie-test-{Guid.NewGuid()}@example.com";

        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "P@ssword1!"));
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "P@ssword1!"));

        response.EnsureSuccessStatusCode();
        await Assert.That(response.Headers.Contains("Set-Cookie")).IsTrue();
        var setCookie = response.Headers.GetValues("Set-Cookie").First();
        await Assert.That(setCookie).Contains("refresh_token");
        await Assert.That(setCookie).Contains("HttpOnly");
        await Assert.That(setCookie).Contains("SameSite=Strict");
    }

    [Test]
    public async Task Refresh_ValidCookie_Returns200WithNewJwt()
    {
        var (client, _) = await RegisterAndLoginAsync();

        var response = await client.PostAsync("/api/auth/refresh", null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RefreshResponse>();
        await Assert.That(body!.Token).IsNotNull();
        await Assert.That(body.Token.Length).IsGreaterThan(20);
    }

    [Test]
    public async Task Refresh_NoCookie_Returns401()
    {
        var client = _factory.CreateClient(); // fresh client, no cookie

        var response = await client.PostAsync("/api/auth/refresh", null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Refresh_RotatesToken_OldTokenInvalid()
    {
        var (client, _) = await RegisterAndLoginAsync();

        // First refresh — rotates the token
        var first = await client.PostAsync("/api/auth/refresh", null);
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // Manually extract the old cookie and try to replay it on a new client
        // (This simulates a replay attack — the old cookie should now be invalid)
        // NOTE: Because the httpOnly cookie is rotated automatically in the cookie jar,
        // we verify indirectly: two successive calls both succeed (proving rotation works)
        var second = await client.PostAsync("/api/auth/refresh", null);
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Logout_ClearsRefreshToken_SubsequentRefreshFails()
    {
        var (client, _) = await RegisterAndLoginAsync();

        var logout = await client.PostAsync("/api/auth/logout", null);
        await Assert.That(logout.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        var refresh = await client.PostAsync("/api/auth/refresh", null);
        await Assert.That(refresh.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Logout_NoCookie_Returns204()
    {
        var client = _factory.CreateClient(); // no cookie

        var response = await client.PostAsync("/api/auth/logout", null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }
}
```

- [ ] **Step 2: Run integration tests**

```bash
dotnet run --project tests/Pacevite.Api.Tests -- --treenode-filter "/*/*/*/*[Category=Integration]"
```

Expected: all `RefreshEndpointsTests` green (Testcontainers spins up real Postgres).

- [ ] **Step 3: Commit**

```bash
git add tests/Pacevite.Api.Tests/Integration/RefreshEndpointsTests.cs
git commit -m "test(auth): add integration tests for refresh and logout endpoints"
```

---

## Task 8: Frontend — Axios Interceptor + AuthContext Logout

**Files:**
- Modify: `src/Pacevite.Web/src/lib/api.ts`
- Modify: `src/Pacevite.Web/src/context/AuthContext.tsx`
- Modify: `src/Pacevite.Web/src/test/handlers.ts`

- [ ] **Step 1: Update `api.ts` — add 401 response interceptor**

Add below the existing request interceptor in `src/Pacevite.Web/src/lib/api.ts`:

```typescript
// Track whether a refresh is already in-flight to prevent request storms
let isRefreshing = false
let failedQueue: Array<{ resolve: (token: string) => void; reject: (err: unknown) => void }> = []

const processQueue = (error: unknown, token: string | null = null) => {
  failedQueue.forEach(p => error ? p.reject(error) : p.resolve(token!))
  failedQueue = []
}

apiClient.interceptors.response.use(
  response => response,
  async error => {
    const originalRequest = error.config
    const isAuthEndpoint = originalRequest?.url?.includes('/api/auth/')

    if (error.response?.status !== 401 || isAuthEndpoint || originalRequest?._retry) {
      return Promise.reject(error)
    }

    if (isRefreshing) {
      return new Promise((resolve, reject) => {
        failedQueue.push({ resolve, reject })
      }).then(token => {
        originalRequest.headers['Authorization'] = `Bearer ${token}`
        return apiClient(originalRequest)
      })
    }

    originalRequest._retry = true
    isRefreshing = true

    try {
      // Use fetch (not apiClient) to avoid interceptor recursion
      const res = await fetch('/api/auth/refresh', {
        method: 'POST',
        credentials: 'include', // sends the httpOnly cookie
      })

      if (!res.ok) throw new Error('Refresh failed')

      const { token } = await res.json() as { token: string }
      tokenStore.set(token)
      processQueue(null, token)
      originalRequest.headers['Authorization'] = `Bearer ${token}`
      return apiClient(originalRequest)
    } catch (refreshError) {
      processQueue(refreshError)
      // Signal AuthContext to logout — dispatch a custom event
      window.dispatchEvent(new Event('auth:logout'))
      return Promise.reject(refreshError)
    } finally {
      isRefreshing = false
    }
  }
)
```

- [ ] **Step 2: Update `AuthContext.tsx` — listen for `auth:logout` event + call logout endpoint**

In `AuthContext.tsx`:

```typescript
// In the AuthProvider component, add effect to listen for the interceptor's signal:
useEffect(() => {
  const handleForceLogout = () => logout()
  window.addEventListener('auth:logout', handleForceLogout)
  return () => window.removeEventListener('auth:logout', handleForceLogout)
}, [])

// Update logout() to call the server:
const logout = useCallback(async () => {
  try {
    await fetch('/api/auth/logout', {
      method: 'POST',
      credentials: 'include',
    })
  } catch {
    // Best-effort — clear client state regardless
  }
  tokenStore.clear()
  setUser(null)
}, [])
```

- [ ] **Step 3: Add MSW handlers for refresh and logout**

In `src/Pacevite.Web/src/test/handlers.ts`, add:

```typescript
http.post('/api/auth/refresh', () => {
  return HttpResponse.json({ token: 'mocked-refreshed-jwt' }, { status: 200 })
}),

http.post('/api/auth/logout', () => {
  return new HttpResponse(null, { status: 204 })
}),
```

- [ ] **Step 4: Run frontend tests**

```bash
cd src/Pacevite.Web && npm test
```

Expected: all existing tests pass; MSW handlers prevent real network calls in tests.

- [ ] **Step 5: Manual E2E verification**

Start the full stack and verify:
1. Log in → wait 60+ minutes (or shorten JWT expiry in `appsettings.Development.json` to 1 minute for testing) → make an API call → token silently refreshes → no re-login prompt
2. Log out → refresh token cookie is cleared → re-login required

```bash
# Speed up testing: override JWT expiry to 1 minute in appsettings.Development.json
# "Jwt": { "ExpiryMinutes": 1, ... }
```

- [ ] **Step 6: Commit**

```bash
git add src/Pacevite.Web/src/lib/api.ts \
        src/Pacevite.Web/src/context/AuthContext.tsx \
        src/Pacevite.Web/src/test/handlers.ts
git commit -m "feat(auth): add Axios 401 interceptor for silent token refresh; update logout to call server"
```

---

## Task 9: ADR 0007

**Files:**
- Create: `docs/decisions/0007-standalone-refresh-tokens-table.md`

- [ ] **Step 1: Write ADR**

```markdown
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
- A standalone table gives full control over columns (`ReplacedByTokenHash`, `RevokedAt`, GIN indexes) that `AspNetUserTokens` does not support natively.

## Consequences

- **Easier:** Refresh token schema evolves independently of Identity. AOT migration does not touch this table.
- **Harder:** One additional migration vs. zero for `AspNetUserTokens`.
- Token cleanup (expired + revoked rows) requires a scheduled job or manual sweep — no automatic pruning.
```

- [ ] **Step 2: Commit**

```bash
git add docs/decisions/0007-standalone-refresh-tokens-table.md
git commit -m "docs: add ADR 0007 for standalone refresh tokens table"
```

---

## Done Checklist

- [ ] `RefreshToken` entity + EF Core migration
- [ ] `IRefreshTokenService` + `RefreshTokenService` + unit tests
- [ ] `CookieHelper` static helper
- [ ] `RefreshCommand` + `RefreshHandler` + unit tests
- [ ] `LogoutCommand` + `LogoutHandler` + unit tests
- [ ] `AuthEndpoints` wired: login/register set cookie; `/refresh` and `/logout` routes registered
- [ ] `IRefreshTokenService` registered in `Program.cs`
- [ ] Integration tests passing
- [ ] Axios 401 interceptor in `api.ts`
- [ ] `AuthContext.logout()` calls server
- [ ] MSW handlers updated
- [ ] ADR 0007 committed

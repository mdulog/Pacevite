# AOT Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `Pacevite.Api` publishable with `PublishAot=true` by replacing reflection-based components with AOT-safe equivalents.

**Architecture:** Replace ASP.NET Core Identity (reflection-heavy) with a custom `AppUser` + `IUserRepository` backed by `IPasswordHasher<AppUser>`. Switch FluentValidation from assembly scanning to explicit registration. Replace reflection-based `JsonSerializer` calls in the JSONB value converter with a source-generated `JsonSerializerContext`. Finish with an EF Core compiled model so the runtime model builder is not required at startup.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API, EF Core 10 (Npgsql), FluentValidation 12, Mediator (source-gen), `Microsoft.Extensions.Identity.Core` (IPasswordHasher only), TUnit, NSubstitute

---

## File Map

| Action | File | Change |
|---|---|---|
| Modify | `src/Pacevite.Api/Pacevite.Api.csproj` | Add AOT flags; swap Identity.EFCore → Identity.Core |
| Create | `src/Pacevite.Api/Infrastructure/Persistence/AppJsonContext.cs` | `[JsonSerializable]` source-gen context |
| Modify | `src/Pacevite.Api/Infrastructure/Persistence/AppDbContext.cs` | Use source-gen converter; remove IdentityDbContext; add AppUser DbSet |
| Create | `src/Pacevite.Api/Domain/Entities/AppUser.cs` | Lightweight user entity (Id, Email, PasswordHash) |
| Create | `src/Pacevite.Api/Infrastructure/Persistence/IUserRepository.cs` | Two-method interface: FindByEmail, Create |
| Create | `src/Pacevite.Api/Infrastructure/Persistence/UserRepository.cs` | EF Core implementation |
| Modify | `src/Pacevite.Api/Infrastructure/Auth/JwtTokenService.cs` | Accept AppUser instead of IdentityUser |
| Modify | `src/Pacevite.Api/Features/Auth/Login/LoginHandler.cs` | Use IUserRepository + IPasswordHasher |
| Modify | `src/Pacevite.Api/Features/Auth/Register/RegisterHandler.cs` | Use IUserRepository + IPasswordHasher |
| Modify | `src/Pacevite.Api/Program.cs` | Explicit validators; remove AddIdentityCore; add IUserRepository + IPasswordHasher |
| Delete+Recreate | `src/Pacevite.Api/Migrations/` | Regenerate without Identity tables |
| Create (generated) | `src/Pacevite.Api/Infrastructure/Persistence/Compiled/` | EF Core compiled model |
| Modify | `tests/Pacevite.Api.Tests/Pacevite.Api.Tests.csproj` | Add NSubstitute |
| Create | `tests/Pacevite.Api.Tests/Unit/Auth/LoginHandlerTests.cs` | Unit tests for LoginHandler |
| Create | `tests/Pacevite.Api.Tests/Unit/Auth/RegisterHandlerTests.cs` | Unit tests for RegisterHandler |

---

## Task 1: Enable AOT Analysis Flags

**Files:**
- Modify: `src/Pacevite.Api/Pacevite.Api.csproj`

The `PublishAot` flag turns on the trimming and AOT analysers at build time. `IsAotCompatible` marks this project as intending to support AOT and propagates that signal to transitive dependencies. No runtime behaviour changes — this just surfaces all remaining warnings so we can track progress through the subsequent tasks.

- [ ] **Step 1: Add AOT flags to csproj**

Replace the existing `<PropertyGroup>` in `src/Pacevite.Api/Pacevite.Api.csproj`:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <PublishAot>true</PublishAot>
  <IsAotCompatible>true</IsAotCompatible>
</PropertyGroup>
```

- [ ] **Step 2: Run build and record warnings**

```bash
dotnet build src/Pacevite.Api
```

Expected: build succeeds with AOT trim warnings. Note how many warnings appear — each subsequent task eliminates a category. Typical warnings at this stage include:
- `IL2026` — reflection-based `JsonSerializer` calls in `AppDbContext`
- `IL2026` — FluentValidation assembly scanning in `Program.cs`
- `IL2026` / `IL3050` — Identity stack (`UserManager`, `IdentityDbContext`)

- [ ] **Step 3: Commit**

```bash
git add src/Pacevite.Api/Pacevite.Api.csproj
git commit -m "build: enable PublishAot and IsAotCompatible flags"
```

---

## Task 2: JsonSerializer Source Generation

**Files:**
- Create: `src/Pacevite.Api/Infrastructure/Persistence/AppJsonContext.cs`
- Modify: `src/Pacevite.Api/Infrastructure/Persistence/AppDbContext.cs`

The `AppDbContext` JSONB value converter uses `JsonSerializer.Serialize/Deserialize` with `null` options, which triggers the reflection-based serializer (`IL2026`). A `JsonSerializerContext` annotated with `[JsonSerializable]` tells the source generator to emit pre-compiled serialization code for `Dictionary<string, object>`, removing the reflection call.

The existing integration tests exercise JSONB round-trips via `Upload_JsonFile_Returns200WithCreatedEvents` (events with location/metadata) and are the verification suite for this task — no new test file needed.

- [ ] **Step 1: Create the source-generated JSON context**

Create `src/Pacevite.Api/Infrastructure/Persistence/AppJsonContext.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Pacevite.Api.Infrastructure.Persistence;

[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSourceGenerationOptions(WriteIndented = false)]
internal sealed partial class AppJsonContext : JsonSerializerContext { }
```

- [ ] **Step 2: Update the value converter in AppDbContext**

In `src/Pacevite.Api/Infrastructure/Persistence/AppDbContext.cs`, replace the `JsonDictConverter` field (lines 17–20):

```csharp
private static readonly ValueConverter<Dictionary<string, object>, string> JsonDictConverter = new(
    v => JsonSerializer.Serialize(v, AppJsonContext.Default.DictionaryStringObject),
    v => JsonSerializer.Deserialize(v, AppJsonContext.Default.DictionaryStringObject) ?? new()
);
```

- [ ] **Step 3: Build and verify the IL2026 warnings from JsonSerializer are gone**

```bash
dotnet build src/Pacevite.Api
```

Expected: the `IL2026` warnings mentioning `JsonSerializer.Serialize` and `JsonSerializer.Deserialize` in `AppDbContext.cs` no longer appear. Identity and FluentValidation warnings remain — those are fixed in later tasks.

- [ ] **Step 4: Run integration tests**

```bash
dotnet test tests/Pacevite.Api.Tests --filter "Category=Integration"
```

Expected: all integration tests pass (JSONB round-trip confirmed working with source-gen serializer).

- [ ] **Step 5: Commit**

```bash
git add src/Pacevite.Api/Infrastructure/Persistence/AppJsonContext.cs \
        src/Pacevite.Api/Infrastructure/Persistence/AppDbContext.cs
git commit -m "feat: replace reflection JsonSerializer with source-gen context for JSONB converter"
```

---

## Task 3: FluentValidation Explicit Registration

**Files:**
- Modify: `src/Pacevite.Api/Program.cs`

`AddValidatorsFromAssemblyContaining<Program>()` uses reflection to scan the assembly for `AbstractValidator<T>` implementations at startup (`IL2026`). Replacing it with four explicit `AddScoped` calls eliminates the scan — the compiler can see exactly which types are needed, and the trimmer will not remove them.

The existing integration tests exercise all four validators (register/login validation, upload validation, delete validation) and are the verification suite.

- [ ] **Step 1: Replace assembly scanning with explicit registrations**

In `src/Pacevite.Api/Program.cs`, replace:

```csharp
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
```

With:

```csharp
builder.Services.AddScoped<IValidator<RegisterCommand>, RegisterValidator>();
builder.Services.AddScoped<IValidator<LoginCommand>, LoginValidator>();
builder.Services.AddScoped<IValidator<UploadEventCommand>, UploadEventValidator>();
builder.Services.AddScoped<IValidator<DeleteEventCommand>, DeleteEventValidator>();
```

Add the missing usings at the top of `Program.cs` (if not already present via implicit usings):

```csharp
using FluentValidation;
using Pacevite.Api.Features.Auth.Login;
using Pacevite.Api.Features.Auth.Register;
using Pacevite.Api.Features.Events.DeleteEvent;
using Pacevite.Api.Features.Events.Upload;
```

- [ ] **Step 2: Build and verify FluentValidation warning is gone**

```bash
dotnet build src/Pacevite.Api
```

Expected: no more `IL2026` warnings mentioning `AddValidatorsFromAssemblyContaining`. Identity warnings remain.

- [ ] **Step 3: Run integration tests**

```bash
dotnet test tests/Pacevite.Api.Tests --filter "Category=Integration"
```

Expected: all integration tests pass. In particular verify:
- `Register_WithInvalidEmail_Returns400` — RegisterValidator still fires
- `Register_WithShortPassword_Returns400` — RegisterValidator still fires

- [ ] **Step 4: Commit**

```bash
git add src/Pacevite.Api/Program.cs
git commit -m "feat: replace FluentValidation assembly scanning with explicit validator registrations for AOT"
```

---

## Task 4: Custom User Store (Replace ASP.NET Core Identity)

**Files:**
- Modify: `tests/Pacevite.Api.Tests/Pacevite.Api.Tests.csproj` — add NSubstitute
- Create: `tests/Pacevite.Api.Tests/Unit/Auth/LoginHandlerTests.cs`
- Create: `tests/Pacevite.Api.Tests/Unit/Auth/RegisterHandlerTests.cs`
- Create: `src/Pacevite.Api/Domain/Entities/AppUser.cs`
- Create: `src/Pacevite.Api/Infrastructure/Persistence/IUserRepository.cs`
- Create: `src/Pacevite.Api/Infrastructure/Persistence/UserRepository.cs`
- Modify: `src/Pacevite.Api/Infrastructure/Persistence/AppDbContext.cs` — remove IdentityDbContext
- Modify: `src/Pacevite.Api/Infrastructure/Auth/JwtTokenService.cs` — AppUser parameter
- Modify: `src/Pacevite.Api/Features/Auth/Login/LoginHandler.cs`
- Modify: `src/Pacevite.Api/Features/Auth/Register/RegisterHandler.cs`
- Modify: `src/Pacevite.Api/Program.cs` — remove Identity, add IUserRepository + IPasswordHasher
- Modify: `src/Pacevite.Api/Pacevite.Api.csproj` — swap packages
- Delete + Recreate: `src/Pacevite.Api/Migrations/`

`UserManager<IdentityUser>` and `IdentityDbContext<IdentityUser>` are the last remaining AOT blockers. The only `UserManager` methods used in this codebase are `FindByEmailAsync`, `CreateAsync`, and `CheckPasswordAsync`. We replace them with a thin `IUserRepository` backed directly by EF Core, and use `IPasswordHasher<AppUser>` (from `Microsoft.Extensions.Identity.Core`) for PBKDF2 hashing — that class is AOT-safe.

### 4a: Add NSubstitute and write failing handler tests

The handler unit tests are written first. They reference `AppUser`, `IUserRepository`, `IPasswordHasher<AppUser>`, and the new constructor signatures for `LoginHandler` / `RegisterHandler` — none of which exist yet. The tests will not compile until Task 4c and 4e/4f are done.

- [ ] **Step 1: Add NSubstitute to test project**

In `tests/Pacevite.Api.Tests/Pacevite.Api.Tests.csproj`, add to the `<ItemGroup>`:

```xml
<PackageReference Include="NSubstitute" Version="5.*" />
```

- [ ] **Step 2: Create LoginHandler unit tests**

Create `tests/Pacevite.Api.Tests/Unit/Auth/LoginHandlerTests.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Features.Auth.Login;
using Pacevite.Api.Infrastructure.Auth;
using Pacevite.Api.Infrastructure.Persistence;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Auth;

[Category("Unit")]
public sealed class LoginHandlerTests
{
    private readonly IUserRepository _repo = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher<AppUser> _hasher = Substitute.For<IPasswordHasher<AppUser>>();
    private readonly IJwtTokenService _tokenService = Substitute.For<IJwtTokenService>();
    private readonly ILogger<LoginHandler> _logger = Substitute.For<ILogger<LoginHandler>>();

    private LoginHandler BuildHandler() => new(_repo, _hasher, _tokenService, _logger);

    [Test]
    public async Task returns_fail_when_user_not_found()
    {
        // Arrange
        _repo.FindByEmailAsync("nobody@example.com", Arg.Any<CancellationToken>())
            .Returns((AppUser?)null);

        // Act
        var result = await BuildHandler().Handle(
            new LoginCommand("nobody@example.com", "P@ssword1!"),
            CancellationToken.None);

        // Assert
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).IsEqualTo("Invalid credentials.");
    }

    [Test]
    public async Task returns_fail_when_password_is_wrong()
    {
        // Arrange
        var user = new AppUser { Email = "user@example.com", PasswordHash = "hashed" };
        _repo.FindByEmailAsync("user@example.com", Arg.Any<CancellationToken>())
            .Returns(user);
        _hasher.VerifyHashedPassword(user, "hashed", "wrongpassword")
            .Returns(PasswordVerificationResult.Failed);

        // Act
        var result = await BuildHandler().Handle(
            new LoginCommand("user@example.com", "wrongpassword"),
            CancellationToken.None);

        // Assert
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).IsEqualTo("Invalid credentials.");
    }

    [Test]
    public async Task returns_ok_with_token_when_credentials_are_valid()
    {
        // Arrange
        var userId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var user = new AppUser { Id = userId, Email = "user@example.com", PasswordHash = "hashed" };
        _repo.FindByEmailAsync("user@example.com", Arg.Any<CancellationToken>())
            .Returns(user);
        _hasher.VerifyHashedPassword(user, "hashed", "P@ssword1!")
            .Returns(PasswordVerificationResult.Success);
        _tokenService.GenerateToken(user).Returns("jwt-token");

        // Act
        var result = await BuildHandler().Handle(
            new LoginCommand("user@example.com", "P@ssword1!"),
            CancellationToken.None);

        // Assert
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Token).IsEqualTo("jwt-token");
        await Assert.That(result.UserId).IsEqualTo(userId.ToString());
    }
}
```

- [ ] **Step 3: Create RegisterHandler unit tests**

Create `tests/Pacevite.Api.Tests/Unit/Auth/RegisterHandlerTests.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Features.Auth.Register;
using Pacevite.Api.Infrastructure.Auth;
using Pacevite.Api.Infrastructure.Persistence;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Auth;

[Category("Unit")]
public sealed class RegisterHandlerTests
{
    private readonly IUserRepository _repo = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher<AppUser> _hasher = Substitute.For<IPasswordHasher<AppUser>>();
    private readonly IJwtTokenService _tokenService = Substitute.For<IJwtTokenService>();
    private readonly ILogger<RegisterHandler> _logger = Substitute.For<ILogger<RegisterHandler>>();

    private RegisterHandler BuildHandler() => new(_repo, _hasher, _tokenService, _logger);

    [Test]
    public async Task returns_fail_duplicate_when_email_already_registered()
    {
        // Arrange
        var existing = new AppUser { Email = "taken@example.com", PasswordHash = "hashed" };
        _repo.FindByEmailAsync("taken@example.com", Arg.Any<CancellationToken>())
            .Returns(existing);

        // Act
        var result = await BuildHandler().Handle(
            new RegisterCommand("taken@example.com", "P@ssword1!"),
            CancellationToken.None);

        // Assert
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.IsDuplicate).IsTrue();
    }

    [Test]
    public async Task returns_ok_with_token_when_registration_succeeds()
    {
        // Arrange
        var userId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        _repo.FindByEmailAsync("new@example.com", Arg.Any<CancellationToken>())
            .Returns((AppUser?)null);
        _hasher.HashPassword(Arg.Any<AppUser>(), "P@ssword1!")
            .Returns("hashed-password");
        var created = new AppUser { Id = userId, Email = "new@example.com", PasswordHash = "hashed-password" };
        _repo.CreateAsync("new@example.com", "hashed-password", Arg.Any<CancellationToken>())
            .Returns(created);
        _tokenService.GenerateToken(created).Returns("jwt-token");

        // Act
        var result = await BuildHandler().Handle(
            new RegisterCommand("new@example.com", "P@ssword1!"),
            CancellationToken.None);

        // Assert
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Token).IsEqualTo("jwt-token");
        await Assert.That(result.UserId).IsEqualTo(userId.ToString());
    }
}
```

- [ ] **Step 4: Run tests — expect compile failure**

```bash
dotnet build tests/Pacevite.Api.Tests
```

Expected: build errors referencing missing types `AppUser`, `IUserRepository`, and missing constructor signatures on `LoginHandler`/`RegisterHandler`. This is correct — the tests are driving the design.

### 4b: Create AppUser entity and IUserRepository interface

- [ ] **Step 5: Create AppUser entity**

Create `src/Pacevite.Api/Domain/Entities/AppUser.cs`:

```csharp
namespace Pacevite.Api.Domain.Entities;

public sealed class AppUser
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Email { get; init; }
    public required string PasswordHash { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 6: Create IUserRepository interface**

Create `src/Pacevite.Api/Infrastructure/Persistence/IUserRepository.cs`:

```csharp
using Pacevite.Api.Domain.Entities;

namespace Pacevite.Api.Infrastructure.Persistence;

public interface IUserRepository
{
    Task<AppUser?> FindByEmailAsync(string email, CancellationToken ct);
    Task<AppUser> CreateAsync(string email, string passwordHash, CancellationToken ct);
}
```

### 4c: Update AppDbContext

`AppDbContext` currently extends `IdentityDbContext<IdentityUser>` which creates 7 Identity tables. We replace it with a plain `DbContext` and a single `Users` table. The `Event` and `EventSplit` configuration blocks are unchanged.

- [ ] **Step 7: Rewrite AppDbContext**

Replace the full contents of `src/Pacevite.Api/Infrastructure/Persistence/AppDbContext.cs`:

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Domain.Enums;

namespace Pacevite.Api.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventSplit> EventSplits => Set<EventSplit>();

    private static readonly ValueConverter<Dictionary<string, object>, string> JsonDictConverter = new(
        v => JsonSerializer.Serialize(v, AppJsonContext.Default.DictionaryStringObject),
        v => JsonSerializer.Deserialize(v, AppJsonContext.Default.DictionaryStringObject) ?? new()
    );

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.HasIndex(u => u.Email).IsUnique();
        });

        builder.Entity<Event>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.EventType)
                .HasConversion<string>();

            entity.Property(e => e.Completion)
                .HasConversion<string>();

            entity.Property(e => e.Location)
                .HasColumnType("jsonb")
                .HasConversion(JsonDictConverter);

            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasConversion(JsonDictConverter);

            entity.HasIndex(e => e.Metadata)
                .HasMethod("GIN");

            entity.HasIndex(e => new { e.UserId, e.EventType });
            entity.HasIndex(e => new { e.UserId, e.EventDate });

            entity.HasMany(e => e.Splits)
                .WithOne(s => s.Event)
                .HasForeignKey(s => s.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<EventSplit>(entity =>
        {
            entity.HasKey(s => s.Id);

            entity.Property(s => s.Metadata)
                .HasColumnType("jsonb")
                .HasConversion(JsonDictConverter);
        });
    }
}
```

### 4d: Update JwtTokenService

- [ ] **Step 8: Update JwtTokenService to accept AppUser**

Replace the full contents of `src/Pacevite.Api/Infrastructure/Auth/JwtTokenService.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Pacevite.Api.Domain.Entities;

namespace Pacevite.Api.Infrastructure.Auth;

public interface IJwtTokenService
{
    string GenerateToken(AppUser user);
}

public sealed class JwtTokenService(IConfiguration configuration) : IJwtTokenService
{
    private const int ExpiryMinutes = 60;

    public string GenerateToken(AppUser user)
    {
        var secret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret is not configured.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(ExpiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

### 4e: Update LoginHandler

- [ ] **Step 9: Rewrite LoginHandler**

Replace the full contents of `src/Pacevite.Api/Features/Auth/Login/LoginHandler.cs`:

```csharp
using Mediator;
using Microsoft.AspNetCore.Identity;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Infrastructure.Auth;
using Pacevite.Api.Infrastructure.Persistence;

namespace Pacevite.Api.Features.Auth.Login;

public sealed class LoginHandler(
    IUserRepository users,
    IPasswordHasher<AppUser> passwordHasher,
    IJwtTokenService jwtTokenService,
    ILogger<LoginHandler> logger) : ICommandHandler<LoginCommand, AuthResult>
{
    public async ValueTask<AuthResult> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(command.Email, cancellationToken);

        // Do not reveal whether the email exists — OWASP A07 (Identification and Authentication Failures)
        if (user is null)
        {
            logger.LogWarning("Failed login attempt for unknown email");
            return AuthResult.Fail("Invalid credentials.");
        }

        var verificationResult = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, command.Password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            logger.LogWarning("Failed login attempt for {UserId}", user.Id);
            return AuthResult.Fail("Invalid credentials.");
        }

        logger.LogInformation("User logged in: {UserId}", user.Id);
        var token = jwtTokenService.GenerateToken(user);
        return AuthResult.Ok(user.Id.ToString(), user.Email, token);
    }
}
```

### 4f: Update RegisterHandler

- [ ] **Step 10: Rewrite RegisterHandler**

Replace the full contents of `src/Pacevite.Api/Features/Auth/Register/RegisterHandler.cs`:

```csharp
using Mediator;
using Microsoft.AspNetCore.Identity;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Infrastructure.Auth;
using Pacevite.Api.Infrastructure.Persistence;

namespace Pacevite.Api.Features.Auth.Register;

public sealed class RegisterHandler(
    IUserRepository users,
    IPasswordHasher<AppUser> passwordHasher,
    IJwtTokenService jwtTokenService,
    ILogger<RegisterHandler> logger) : ICommandHandler<RegisterCommand, AuthResult>
{
    public async ValueTask<AuthResult> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        var existing = await users.FindByEmailAsync(command.Email, cancellationToken);
        if (existing is not null)
            return AuthResult.FailDuplicate("Email is already registered.");

        // PasswordHasher<T> does not use the user object in its default implementation —
        // the parameter exists for custom implementations that embed user-specific data.
        // We pass a placeholder here since the real user has not been persisted yet.
        var placeholder = new AppUser { Email = command.Email, PasswordHash = string.Empty };
        var passwordHash = passwordHasher.HashPassword(placeholder, command.Password);

        var user = await users.CreateAsync(command.Email, passwordHash, cancellationToken);

        logger.LogInformation("User registered: {UserId}", user.Id);
        var token = jwtTokenService.GenerateToken(user);
        return AuthResult.Ok(user.Id.ToString(), user.Email, token);
    }
}
```

### 4g: Create UserRepository

- [ ] **Step 11: Implement UserRepository**

Create `src/Pacevite.Api/Infrastructure/Persistence/UserRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Pacevite.Api.Domain.Entities;

namespace Pacevite.Api.Infrastructure.Persistence;

public sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<AppUser?> FindByEmailAsync(string email, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(
            u => u.Email == email.ToLowerInvariant(), ct);

    public async Task<AppUser> CreateAsync(string email, string passwordHash, CancellationToken ct)
    {
        var user = new AppUser
        {
            Email = email.ToLowerInvariant(),
            PasswordHash = passwordHash
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }
}
```

### 4h: Update Program.cs

- [ ] **Step 12: Remove Identity, add IUserRepository and IPasswordHasher**

In `src/Pacevite.Api/Program.cs`, remove the entire `// ── Identity ──` block:

```csharp
// DELETE these lines:
builder.Services.AddIdentityCore<IdentityUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = true;
    })
    .AddEntityFrameworkStores<AppDbContext>();
```

Add the following after the `// ── Database ──` block:

```csharp
// ── User Store ────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
```

Add usings at the top of `Program.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Infrastructure.Persistence;
```

Remove the `using Microsoft.AspNetCore.Identity;` line that was used by the Identity block (it can be kept if still needed by `PasswordHasher<AppUser>` — it is, so keep it).

The final `Program.cs` `// ── Services ──` section should look like:

```csharp
// ── User Store ────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

// ── Event Parsers (registered as IEventParser — endpoint dispatches by content type) ──
builder.Services.AddSingleton<IEventParser, CsvEventParser>();
builder.Services.AddSingleton<IEventParser, JsonEventParser>();
```

### 4i: Swap NuGet packages

- [ ] **Step 13: Update csproj — remove Identity.EFCore, add Identity.Core**

In `src/Pacevite.Api/Pacevite.Api.csproj`, replace:

```xml
<PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="10.*" />
```

With:

```xml
<PackageReference Include="Microsoft.Extensions.Identity.Core" Version="10.*" />
```

### 4j: Regenerate migrations

- [ ] **Step 14: Delete old migrations and regenerate**

```bash
rm -rf src/Pacevite.Api/Migrations
dotnet ef migrations add InitialCreate --project src/Pacevite.Api
```

Expected: a new `Migrations/` folder is created. The generated migration should contain:
- `CreateTable("AppUsers", ...)` — Id (uuid), Email (text, unique index), PasswordHash (text), CreatedAt (timestamp)
- `CreateTable("Events", ...)` — same as before
- `CreateTable("EventSplits", ...)` — same as before
- **No** `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, etc.

Verify the migration looks correct before proceeding.

- [ ] **Step 15: Run unit tests — expect all to pass**

```bash
dotnet test tests/Pacevite.Api.Tests --filter "Category=Unit"
```

Expected: all unit tests pass, including the new `LoginHandlerTests` and `RegisterHandlerTests`.

- [ ] **Step 16: Run integration tests — expect all to pass**

```bash
dotnet test tests/Pacevite.Api.Tests --filter "Category=Integration"
```

Expected: all integration tests pass. The Testcontainers test setup calls `db.Database.Migrate()` which applies the new clean migration, creating the `AppUsers` table instead of the Identity tables.

- [ ] **Step 17: Verify Identity AOT warnings are gone**

```bash
dotnet build src/Pacevite.Api
```

Expected: all AOT warnings from the Identity stack are gone. At this point, zero `IL2026`/`IL3050` warnings should remain.

- [ ] **Step 18: Commit**

```bash
git add \
  src/Pacevite.Api/Domain/Entities/AppUser.cs \
  src/Pacevite.Api/Infrastructure/Persistence/IUserRepository.cs \
  src/Pacevite.Api/Infrastructure/Persistence/UserRepository.cs \
  src/Pacevite.Api/Infrastructure/Persistence/AppDbContext.cs \
  src/Pacevite.Api/Infrastructure/Auth/JwtTokenService.cs \
  src/Pacevite.Api/Features/Auth/Login/LoginHandler.cs \
  src/Pacevite.Api/Features/Auth/Register/RegisterHandler.cs \
  src/Pacevite.Api/Program.cs \
  src/Pacevite.Api/Pacevite.Api.csproj \
  src/Pacevite.Api/Migrations/ \
  tests/Pacevite.Api.Tests/Pacevite.Api.Tests.csproj \
  tests/Pacevite.Api.Tests/Unit/Auth/LoginHandlerTests.cs \
  tests/Pacevite.Api.Tests/Unit/Auth/RegisterHandlerTests.cs
git commit -m "feat: replace ASP.NET Core Identity with custom AppUser + IUserRepository for AOT compatibility"
```

---

## Task 5: EF Core Compiled Model

**Files:**
- New (generated): `src/Pacevite.Api/Infrastructure/Persistence/Compiled/` — EF Core generated files
- Modify: `src/Pacevite.Api/Program.cs` — `UseModel(AppDbContextModel.Instance)`

The EF Core runtime model builder uses reflection at startup to discover entity configurations. `dotnet ef dbcontext optimize` generates a pre-built compiled model that replaces this. The model is now stable (Task 4 is done), so this is the right time to generate it.

**Important:** The compiled model must be regenerated whenever entity configuration changes (new entities, properties, indexes, relationships). Add `dotnet ef dbcontext optimize` to your development workflow alongside `dotnet ef migrations add`.

The integration tests replace `DbContextOptions<AppDbContext>` and do **not** call `UseModel` — they use the runtime model builder, which still works in non-AOT (JIT) test runs. No test changes are needed.

- [ ] **Step 1: Generate the compiled model**

```bash
dotnet ef dbcontext optimize \
  --project src/Pacevite.Api \
  --output-dir Infrastructure/Persistence/Compiled \
  --namespace Pacevite.Api.Infrastructure.Persistence.Compiled
```

Expected: several `.cs` files are created in `src/Pacevite.Api/Infrastructure/Persistence/Compiled/`. The primary file is `AppDbContextModel.cs` containing a class named `AppDbContextModel` with a static `Instance` property.

- [ ] **Step 2: Wire the compiled model into Program.cs**

In `src/Pacevite.Api/Program.cs`, update the `AddDbContext` registration:

```csharp
using Pacevite.Api.Infrastructure.Persistence.Compiled;

// Replace:
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// With:
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
           .UseModel(AppDbContextModel.Instance));
```

- [ ] **Step 3: Build the project**

```bash
dotnet build src/Pacevite.Api
```

Expected: clean build, zero warnings. The compiled model satisfies EF Core's AOT requirement.

- [ ] **Step 4: Run all tests**

```bash
dotnet test tests/Pacevite.Api.Tests
```

Expected: all unit and integration tests pass. Integration tests continue to use the runtime model builder (they override `DbContextOptions` without `UseModel`) — this is intentional and correct for non-AOT test runs.

- [ ] **Step 5: Verify publish succeeds**

```bash
dotnet publish src/Pacevite.Api -r linux-arm64 --self-contained
```

Use the target runtime for your deployment. Expected: publish succeeds with no AOT errors. The output in `bin/Release/net10.0/linux-arm64/publish/` will contain a native binary.

- [ ] **Step 6: Commit**

```bash
git add \
  src/Pacevite.Api/Infrastructure/Persistence/Compiled/ \
  src/Pacevite.Api/Program.cs
git commit -m "feat: add EF Core compiled model and wire UseModel for AOT publish"
```

---

## Self-Review

**Spec coverage check:**

| Requirement | Task |
|---|---|
| Enable AOT analysis flags | Task 1 |
| JsonSerializer source gen for JSONB converter | Task 2 |
| FluentValidation explicit registration | Task 3 |
| Replace ASP.NET Core Identity | Task 4 |
| EF Core compiled model + external migrations | Task 5 |
| Tests for new handler logic | Task 4a (LoginHandlerTests, RegisterHandlerTests) |
| All existing tests still pass | Verified in Task 3, 4j, 5 |
| Zero AOT warnings at end | Verified in Task 4j Step 17 + Task 5 Step 3 |
| Successful native publish | Task 5 Step 5 |

**No gaps found.**

**Type consistency check:**

- `AppUser` — defined in Task 4b, used in Task 4c/4d/4e/4f/4g/4h and unit tests. ✅
- `IUserRepository.CreateAsync(string email, string passwordHash, CancellationToken ct)` — defined in Task 4b, implemented in Task 4g, called in Task 4f (RegisterHandler). ✅
- `IUserRepository.FindByEmailAsync(string email, CancellationToken ct)` — defined in Task 4b, implemented in Task 4g, called in Task 4e (LoginHandler) and Task 4f (RegisterHandler). ✅
- `IJwtTokenService.GenerateToken(AppUser user)` — updated in Task 4d, called in Task 4e and 4f, mocked in unit tests. ✅
- `AppDbContextModel.Instance` — generated in Task 5 Step 1, used in Task 5 Step 2. ✅
- `AppJsonContext.Default.DictionaryStringObject` — defined in Task 2 Step 1, used in Task 2 Step 2 and Task 4c. ✅

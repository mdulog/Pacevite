using Mediator;

namespace Pacevite.Api.Features.Auth.Logout;

public sealed record LogoutCommand(string? RawToken) : ICommand<bool>;

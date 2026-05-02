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

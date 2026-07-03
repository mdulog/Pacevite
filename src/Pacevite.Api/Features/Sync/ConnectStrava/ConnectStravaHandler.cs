using System.Web;
using Mediator;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Pacevite.Api.Infrastructure.Sync;

namespace Pacevite.Api.Features.Sync.ConnectStrava;

public sealed class ConnectStravaHandler(
    IOptions<StravaOptions> options,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<ConnectStravaHandler> logger) : IQueryHandler<ConnectStravaQuery, string>
{
    private const string AuthorizeBaseUrl = "https://www.strava.com/oauth/authorize";

    public ValueTask<string> Handle(ConnectStravaQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var stravaOptions = options.Value;
            var state = StravaState.Create(dataProtectionProvider, query.UserId);

            var url = $"{AuthorizeBaseUrl}"
                + $"?client_id={HttpUtility.UrlEncode(stravaOptions.ClientId)}"
                + $"&redirect_uri={HttpUtility.UrlEncode(stravaOptions.RedirectUri)}"
                + "&response_type=code"
                + "&approval_prompt=auto"
                + "&scope=activity%3Aread_all"
                + $"&state={HttpUtility.UrlEncode(state)}";

            logger.LogInformation("Strava authorize URL generated for {UserId}", query.UserId);
            return ValueTask.FromResult(url);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "ConnectStravaHandler failed for {UserId}", query.UserId);
            throw;
        }
    }
}

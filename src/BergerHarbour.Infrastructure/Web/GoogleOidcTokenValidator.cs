using Google.Apis.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Infrastructure.Web;

/// <summary>Validates Cloud Scheduler's Google OIDC token: issuer Google, audience = service URL, email = the scheduler account.</summary>
public sealed class GoogleOidcTokenValidator
{
    private readonly SchedulerOidcOptions options;
    private readonly ILogger<GoogleOidcTokenValidator> logger;

    public GoogleOidcTokenValidator(IOptions<SchedulerOidcOptions> options, ILogger<GoogleOidcTokenValidator> logger)
    {
        this.options = options.Value;
        this.logger = logger;
    }

    public async Task<bool> ValidateAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(token, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [options.Audience],
            });
            return payload.EmailVerified && string.Equals(payload.Email, options.ServiceAccountEmail, StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidJwtException e)
        {
            logger.LogWarning("Rejected a jobs OIDC token: {Reason}", e.Message);
            return false;
        }
    }
}

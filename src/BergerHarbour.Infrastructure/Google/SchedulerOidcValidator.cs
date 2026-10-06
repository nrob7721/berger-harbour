using Google.Apis.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Infrastructure.Google;

public sealed class SchedulerOidcOptions
{
    /// <summary>SCHEDULER_SERVICE_ACCOUNT_EMAIL</summary>
    public string ServiceAccountEmail { get; set; } = string.Empty;

    /// <summary>SCHEDULER_AUDIENCE: the admin-api Cloud Run service URL the job calls.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Development only: accept the jobs endpoint without a token.</summary>
    public bool DevBypass { get; set; }
}

/// <summary>
/// Validates the Google-signed OIDC token Cloud Scheduler attaches: issuer Google, audience = the service URL and
/// email = the Scheduler service account.
/// </summary>
public sealed class SchedulerOidcValidator(IOptions<SchedulerOidcOptions> options, ILogger<SchedulerOidcValidator> logger)
{
    public async Task<bool> ValidateAsync(string? bearerToken)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            return false;
        }

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(bearerToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [o.Audience],
                IssuedAtClockTolerance = TimeSpan.FromMinutes(1),
                ExpirationTimeClockTolerance = TimeSpan.FromMinutes(1),
            });
            var ok = payload.EmailVerified &&
                     string.Equals(payload.Email, o.ServiceAccountEmail, StringComparison.OrdinalIgnoreCase);
            if (!ok)
            {
                logger.LogWarning("Jobs endpoint called by unexpected identity {Email}", payload.Email);
            }

            return ok;
        }
        catch (InvalidJwtException ex)
        {
            logger.LogWarning("Jobs endpoint rejected an OIDC token: {Reason}", ex.Message);
            return false;
        }
    }
}

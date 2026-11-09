using Google.Apis.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Infrastructure.Google;

public sealed class SchedulerOptions
{
    public string ServiceAccountEmail { get; set; } = "";
    /// <summary>The admin-api service URL the Scheduler job uses as the token audience.</summary>
    public string Audience { get; set; } = "";
}

/// <summary>Validates the Google OIDC token Cloud Scheduler sends: issuer Google, audience = service URL, email = the Scheduler service account.</summary>
public sealed class SchedulerOidcValidator(IOptions<SchedulerOptions> options, ILogger<SchedulerOidcValidator> logger)
{
    public async Task<bool> ValidateAsync(string? bearerToken)
    {
        if (string.IsNullOrWhiteSpace(bearerToken)) return false;
        var o = options.Value;
        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(bearerToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [o.Audience, o.Audience.TrimEnd('/') + "/api/jobs/process-notifications"],
            });
            var ok = payload.EmailVerified && string.Equals(payload.Email, o.ServiceAccountEmail, StringComparison.OrdinalIgnoreCase);
            if (!ok) logger.LogWarning("Scheduler token email {Email} is not the Scheduler service account", payload.Email);
            return ok;
        }
        catch (InvalidJwtException e)
        {
            logger.LogWarning("Rejected a Scheduler token: {Message}", e.Message);
            return false;
        }
    }
}

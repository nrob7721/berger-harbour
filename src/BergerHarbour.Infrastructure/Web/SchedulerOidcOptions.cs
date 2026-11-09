namespace BergerHarbour.Infrastructure.Web;

/// <summary>The Cloud Scheduler identity the jobs endpoint accepts.</summary>
public sealed class SchedulerOidcOptions
{
    public string ServiceAccountEmail { get; set; } = string.Empty;

    /// <summary>The admin-api service URL the OIDC token is minted for.</summary>
    public string Audience { get; set; } = string.Empty;
}

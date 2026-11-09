namespace BergerHarbour.Infrastructure.Cloudflare;

/// <summary>The Access application the admin API trusts.</summary>
public sealed class CloudflareAccessOptions
{
    /// <summary>e.g. <c>berger.cloudflareaccess.com</c>.</summary>
    public string TeamDomain { get; set; } = string.Empty;

    /// <summary>The Access application's AUD tag.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Comma-separated staff emails (<c>ADMIN_ALLOWED_EMAILS</c>).</summary>
    public string AllowedEmails { get; set; } = string.Empty;

    public IReadOnlySet<string> AllowedEmailSet =>
        AllowedEmails.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

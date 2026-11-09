using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BergerHarbour.Infrastructure.Cloudflare;

public sealed class AccessOptions
{
    /// <summary>e.g. berger.cloudflareaccess.com</summary>
    public string TeamDomain { get; set; } = "";
    /// <summary>The Access application's AUD tag.</summary>
    public string Audience { get; set; } = "";
    /// <summary>Comma-separated staff emails (ADMIN_ALLOWED_EMAILS).</summary>
    public string AllowedEmails { get; set; } = "";

    public string Issuer => "https://" + TeamDomain.Replace("https://", "").TrimEnd('/');
    public string CertsUrl => Issuer + "/cdn-cgi/access/certs";

    public IReadOnlySet<string> AllowedEmailSet =>
        AllowedEmails.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.ToLowerInvariant()).ToHashSet();
}

/// <summary>Validates the Cf-Access-Jwt-Assertion header against the team's JWKS: signature, expiry, issuer and audience.</summary>
public sealed class AccessJwtValidator(HttpClient http, IOptions<AccessOptions> options, ILogger<AccessJwtValidator> logger)
{
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private JsonWebKeySet? _keys;
    private DateTimeOffset _fetchedAt;

    /// <summary>The authenticated, allow-listed staff email, or null.</summary>
    public async Task<string?> ValidateAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var o = options.Value;
        var result = await ValidateWithKeysAsync(token, await KeysAsync(false, ct));
        if (result is { IsValid: false, Exception: SecurityTokenSignatureKeyNotFoundException })
            result = await ValidateWithKeysAsync(token, await KeysAsync(true, ct)); // keys rotated
        if (!result.IsValid)
        {
            logger.LogWarning("Rejected an Access JWT: {Reason}", result.Exception?.GetType().Name);
            return null;
        }
        var email = result.Claims.TryGetValue("email", out var e) ? e?.ToString()?.ToLowerInvariant() : null;
        if (email is null || !o.AllowedEmailSet.Contains(email))
        {
            logger.LogWarning("Access JWT email is not in ADMIN_ALLOWED_EMAILS");
            return null;
        }
        return email;
    }

    private Task<TokenValidationResult> ValidateWithKeysAsync(string token, JsonWebKeySet keys) =>
        new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = options.Value.Issuer,
            ValidAudience = options.Value.Audience,
            IssuerSigningKeys = keys.GetSigningKeys(),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            RequireSignedTokens = true,
        });

    private async Task<JsonWebKeySet> KeysAsync(bool force, CancellationToken ct)
    {
        if (!force && _keys is not null && DateTimeOffset.UtcNow - _fetchedAt < TimeSpan.FromHours(1)) return _keys;
        await _refresh.WaitAsync(ct);
        try
        {
            if (!force && _keys is not null && DateTimeOffset.UtcNow - _fetchedAt < TimeSpan.FromHours(1)) return _keys;
            var json = await http.GetStringAsync(options.Value.CertsUrl, ct);
            _keys = new JsonWebKeySet(json);
            _fetchedAt = DateTimeOffset.UtcNow;
            return _keys;
        }
        finally
        {
            _refresh.Release();
        }
    }
}

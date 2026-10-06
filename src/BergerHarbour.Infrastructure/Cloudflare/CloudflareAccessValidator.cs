using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BergerHarbour.Infrastructure.Cloudflare;

public sealed class CloudflareAccessOptions
{
    /// <summary>CF_ACCESS_TEAM_DOMAIN, e.g. bergerhouseboats.cloudflareaccess.com</summary>
    public string TeamDomain { get; set; } = string.Empty;

    /// <summary>CF_ACCESS_AUD: the Access application's AUD tag.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>ADMIN_ALLOWED_EMAILS, comma-separated.</summary>
    public string AllowedEmails { get; set; } = string.Empty;

    /// <summary>Development only: skip JWT validation and act as this email.</summary>
    public string? DevBypassEmail { get; set; }

    public IReadOnlySet<string> AllowedEmailSet =>
        AllowedEmails.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.ToLowerInvariant()).ToHashSet();

    public string Issuer => $"https://{TeamDomain.Trim().TrimEnd('/').Replace("https://", string.Empty)}";
}

public sealed record AccessValidationResult(bool IsValid, string? Email, string? Error);

/// <summary>
/// Validates the Cf-Access-Jwt-Assertion header against the team's JWKS
/// (https://&lt;team&gt;.cloudflareaccess.com/cdn-cgi/access/certs): signature, expiry, issuer and the application AUD,
/// then checks the email claim against ADMIN_ALLOWED_EMAILS.
/// </summary>
public sealed class CloudflareAccessValidator(HttpClient http, IOptions<CloudflareAccessOptions> options,
    ILogger<CloudflareAccessValidator> logger)
{
    private static readonly TimeSpan KeyCacheLifetime = TimeSpan.FromHours(1);
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private IReadOnlyList<SecurityKey> _keys = [];
    private DateTimeOffset _keysFetchedAt = DateTimeOffset.MinValue;

    public async Task<AccessValidationResult> ValidateAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new AccessValidationResult(false, null, "Missing Cloudflare Access token.");
        }

        var o = options.Value;
        var result = await ValidateWithKeysAsync(token, await GetKeysAsync(false, ct));
        if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            // Keys rotate; refresh once.
            result = await ValidateWithKeysAsync(token, await GetKeysAsync(true, ct));
        }

        if (!result.IsValid)
        {
            logger.LogWarning("Rejected Cloudflare Access token: {Reason}", result.Exception?.Message);
            return new AccessValidationResult(false, null, "Invalid Cloudflare Access token.");
        }

        var email = (result.Claims.TryGetValue("email", out var e) ? e?.ToString() : null)?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email) || !o.AllowedEmailSet.Contains(email))
        {
            logger.LogWarning("Cloudflare Access user {Email} is not in ADMIN_ALLOWED_EMAILS", email);
            return new AccessValidationResult(false, email, "This account is not allowed to use the admin app.");
        }

        return new AccessValidationResult(true, email, null);
    }

    private Task<TokenValidationResult> ValidateWithKeysAsync(string token, IReadOnlyList<SecurityKey> keys)
    {
        var o = options.Value;
        return new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = o.Issuer,
            ValidAudience = o.Audience,
            IssuerSigningKeys = keys,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        });
    }

    private async Task<IReadOnlyList<SecurityKey>> GetKeysAsync(bool force, CancellationToken ct)
    {
        if (!force && _keys.Count > 0 && DateTimeOffset.UtcNow - _keysFetchedAt < KeyCacheLifetime)
        {
            return _keys;
        }

        await _refresh.WaitAsync(ct);
        try
        {
            if (!force && _keys.Count > 0 && DateTimeOffset.UtcNow - _keysFetchedAt < KeyCacheLifetime)
            {
                return _keys;
            }

            var json = await http.GetStringAsync($"{options.Value.Issuer}/cdn-cgi/access/certs", ct);
            using var doc = JsonDocument.Parse(json);
            var jwks = new JsonWebKeySet(doc.RootElement.TryGetProperty("keys", out _) ? json : "{\"keys\":[]}");
            _keys = jwks.GetSigningKeys().ToList();
            _keysFetchedAt = DateTimeOffset.UtcNow;
            return _keys;
        }
        finally
        {
            _refresh.Release();
        }
    }
}

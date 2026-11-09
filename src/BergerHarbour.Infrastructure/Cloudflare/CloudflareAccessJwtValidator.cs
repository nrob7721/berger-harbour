using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BergerHarbour.Infrastructure.Cloudflare;

/// <summary>
/// Validates <c>Cf-Access-Jwt-Assertion</c> against the team's JWKS: signature, expiry, issuer and <c>aud</c>. Returns the
/// <c>email</c> claim when valid and on the allow-list.
/// </summary>
public sealed class CloudflareAccessJwtValidator
{
    private static readonly TimeSpan KeysLifetime = TimeSpan.FromHours(1);

    private readonly HttpClient http;
    private readonly CloudflareAccessOptions options;
    private readonly ILogger<CloudflareAccessJwtValidator> logger;
    private readonly SemaphoreSlim refresh = new(1, 1);
    private IReadOnlyList<SecurityKey> keys = [];
    private DateTimeOffset keysFetchedAt = DateTimeOffset.MinValue;

    public CloudflareAccessJwtValidator(HttpClient http, IOptions<CloudflareAccessOptions> options, ILogger<CloudflareAccessJwtValidator> logger)
    {
        this.http = http;
        this.options = options.Value;
        this.logger = logger;
    }

    private string Issuer => "https://" + options.TeamDomain.Trim().TrimEnd('/').Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase);

    public async Task<string?> ValidateAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var result = await ValidateWithKeysAsync(token, await KeysAsync(false, ct));
        if (result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            result = await ValidateWithKeysAsync(token, await KeysAsync(true, ct)); // keys rotated
        }

        if (!result.IsValid)
        {
            logger.LogWarning("Rejected Access JWT: {Reason}", result.Exception?.Message);
            return null;
        }

        var email = result.Claims.TryGetValue("email", out var value) ? value?.ToString()?.Trim().ToLowerInvariant() : null;
        if (email is null || !options.AllowedEmailSet.Contains(email))
        {
            logger.LogWarning("Rejected Access JWT: the email is not on ADMIN_ALLOWED_EMAILS.");
            return null;
        }

        return email;
    }

    private Task<TokenValidationResult> ValidateWithKeysAsync(string token, IReadOnlyList<SecurityKey> signingKeys) =>
        new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKeys = signingKeys,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        });

    private async Task<IReadOnlyList<SecurityKey>> KeysAsync(bool force, CancellationToken ct)
    {
        if (!force && keys.Count > 0 && DateTimeOffset.UtcNow - keysFetchedAt < KeysLifetime)
        {
            return keys;
        }

        await refresh.WaitAsync(ct);
        try
        {
            var json = await http.GetStringAsync($"{Issuer}/cdn-cgi/access/certs", ct);
            keys = new JsonWebKeySet(json).GetSigningKeys().ToList();
            keysFetchedAt = DateTimeOffset.UtcNow;
            return keys;
        }
        finally
        {
            refresh.Release();
        }
    }
}

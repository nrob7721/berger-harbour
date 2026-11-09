namespace BergerHarbour.Infrastructure.Turnstile;

public sealed class TurnstileOptions
{
    /// <summary>Cloudflare's always-pass test secret, used only in Local when no secret is set.</summary>
    public const string AlwaysPassTestSecret = "1x0000000000000000000000000000000AA";

    public string Secret { get; set; } = string.Empty;

    public string VerifyUrl { get; set; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
}

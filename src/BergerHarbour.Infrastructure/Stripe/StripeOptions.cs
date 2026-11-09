namespace BergerHarbour.Infrastructure.Stripe;

/// <summary>Stripe secrets (from Secret Manager in deployed environments).</summary>
public sealed class StripeOptions
{
    public string SecretKey { get; set; } = string.Empty;

    public string WebhookSecret { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);
}

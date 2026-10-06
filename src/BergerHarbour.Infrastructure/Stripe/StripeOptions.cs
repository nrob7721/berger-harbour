namespace BergerHarbour.Infrastructure.Stripe;

public sealed class StripeOptions
{
    /// <summary>STRIPE_SECRET_KEY</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>STRIPE_WEBHOOK_SECRET</summary>
    public string WebhookSecret { get; set; } = string.Empty;
}

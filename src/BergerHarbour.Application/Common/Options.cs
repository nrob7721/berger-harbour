namespace BergerHarbour.Application.Common;

/// <summary>Notification timing, from environment variables (Notifications__*). Shorten for testing.</summary>
public sealed class NotificationOptions
{
    public const string Section = "Notifications";

    public TimeSpan BalanceDueBeforeHire { get; set; } = TimeSpan.FromDays(30);

    public TimeSpan ExtendedFirstInstalmentBeforeHire { get; set; } = TimeSpan.FromDays(120);

    public TimeSpan ExtendedFinalInstalmentBeforeHire { get; set; } = TimeSpan.FromDays(90);

    public TimeSpan PaymentReminderBeforeDue { get; set; } = TimeSpan.FromDays(7);

    public TimeSpan PreHireInstructionsBeforeHire { get; set; } = TimeSpan.FromDays(7);

    public IEnumerable<string> Validate()
    {
        foreach (var (name, value) in new[]
                 {
                     (nameof(BalanceDueBeforeHire), BalanceDueBeforeHire),
                     (nameof(ExtendedFirstInstalmentBeforeHire), ExtendedFirstInstalmentBeforeHire),
                     (nameof(ExtendedFinalInstalmentBeforeHire), ExtendedFinalInstalmentBeforeHire),
                     (nameof(PaymentReminderBeforeDue), PaymentReminderBeforeDue),
                     (nameof(PreHireInstructionsBeforeHire), PreHireInstructionsBeforeHire),
                 })
        {
            if (value < TimeSpan.Zero)
            {
                yield return $"Notifications__{name} must not be negative.";
            }
        }

        if (ExtendedFirstInstalmentBeforeHire < ExtendedFinalInstalmentBeforeHire)
        {
            yield return "Notifications__ExtendedFirstInstalmentBeforeHire must be at least ExtendedFinalInstalmentBeforeHire.";
        }
    }
}

/// <summary>Hold timing, from environment variables (Booking__*).</summary>
public sealed class BookingOptions
{
    public const string Section = "Booking";

    /// <summary>Stripe requires a checkout session to live at least 30 minutes.</summary>
    public static readonly TimeSpan StripeMinimumSessionLifetime = TimeSpan.FromMinutes(30);

    public TimeSpan PendingHoldDuration { get; set; } = TimeSpan.FromMinutes(30);

    public TimeSpan PendingHoldGrace { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How long the Stripe checkout session lives: max(PendingHoldDuration, 30 min).</summary>
    public TimeSpan CheckoutSessionLifetime =>
        PendingHoldDuration > StripeMinimumSessionLifetime ? PendingHoldDuration : StripeMinimumSessionLifetime;

    /// <summary>How long the dates are held: the checkout lifetime plus the grace period.</summary>
    public TimeSpan HoldDuration => CheckoutSessionLifetime + PendingHoldGrace;

    public IEnumerable<string> Validate()
    {
        if (PendingHoldDuration <= TimeSpan.Zero)
        {
            yield return "Booking__PendingHoldDuration must be positive.";
        }

        if (PendingHoldDuration > TimeSpan.FromHours(23))
        {
            yield return "Booking__PendingHoldDuration must be under 23 hours (Stripe's session limit is 24 hours).";
        }

        if (PendingHoldGrace < TimeSpan.FromMinutes(2))
        {
            yield return "Booking__PendingHoldGrace must be at least 2 minutes.";
        }
    }
}

/// <summary>Payment links: https://book.bergerhouseboats.com.au/pay/?token=…</summary>
public sealed class PaymentLinkOptions
{
    /// <summary>PUBLIC_BOOKING_BASE_URL, e.g. https://book.bergerhouseboats.com.au</summary>
    public string PublicBookingBaseUrl { get; set; } = "https://book.bergerhouseboats.com.au";

    /// <summary>PAYMENT_LINK_SECRET: HMAC key that derives each booking's stable payment-link token.</summary>
    public string Secret { get; set; } = string.Empty;

    public IEnumerable<string> Validate()
    {
        if (!Uri.TryCreate(PublicBookingBaseUrl, UriKind.Absolute, out _))
        {
            yield return "PUBLIC_BOOKING_BASE_URL must be an absolute URL.";
        }

        if (Secret.Length < 32)
        {
            yield return "PAYMENT_LINK_SECRET must be at least 32 characters.";
        }
    }
}

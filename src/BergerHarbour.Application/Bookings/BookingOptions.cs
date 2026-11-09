namespace BergerHarbour.Application.Bookings;

/// <summary><c>Booking__*</c> timing settings (§10.2).</summary>
public sealed class BookingOptions
{
    public const string Section = "Booking";

    /// <summary>Stripe's minimum Checkout session lifetime.</summary>
    public static readonly TimeSpan StripeMinimumSession = TimeSpan.FromMinutes(30);

    public TimeSpan PendingHoldDuration { get; set; } = TimeSpan.FromMinutes(30);

    public TimeSpan PendingHoldGrace { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan AbandonedDraftAfter { get; set; } = TimeSpan.FromHours(2);

    public TimeSpan DraftRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>The Checkout session lifetime: the configured hold, at least Stripe's 30 minutes.</summary>
    public TimeSpan CheckoutSessionLifetime => PendingHoldDuration < StripeMinimumSession ? StripeMinimumSession : PendingHoldDuration;

    /// <summary>How long a hold blocks the dates: the session lifetime plus the grace period.</summary>
    public TimeSpan HoldLifetime => CheckoutSessionLifetime + PendingHoldGrace;
}

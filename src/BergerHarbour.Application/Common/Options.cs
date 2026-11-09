using System.Security.Cryptography;
using System.Text;
using BergerHarbour.Domain.Bookings;

namespace BergerHarbour.Application.Common;

/// <summary>Notification timing (§10.2). Bound from the "Notifications" section.</summary>
public sealed class NotificationOptions
{
    public const string Section = "Notifications";
    public TimeSpan BalanceDueBeforeHire { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan ExtendedFirstInstalmentBeforeHire { get; set; } = TimeSpan.FromDays(120);
    public TimeSpan ExtendedFinalInstalmentBeforeHire { get; set; } = TimeSpan.FromDays(90);
    public TimeSpan PaymentReminderBeforeDue { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan PreHireInstructionsBeforeHire { get; set; } = TimeSpan.FromDays(7);

    public PaymentScheduleOptions ToScheduleOptions() =>
        new(BalanceDueBeforeHire, ExtendedFirstInstalmentBeforeHire, ExtendedFinalInstalmentBeforeHire, PaymentReminderBeforeDue);

    public IEnumerable<string> Validate()
    {
        foreach (var (name, v) in new[]
                 {
                     (nameof(BalanceDueBeforeHire), BalanceDueBeforeHire), (nameof(ExtendedFirstInstalmentBeforeHire), ExtendedFirstInstalmentBeforeHire),
                     (nameof(ExtendedFinalInstalmentBeforeHire), ExtendedFinalInstalmentBeforeHire), (nameof(PaymentReminderBeforeDue), PaymentReminderBeforeDue),
                     (nameof(PreHireInstructionsBeforeHire), PreHireInstructionsBeforeHire),
                 })
            if (v < TimeSpan.Zero) yield return $"Notifications__{name} can't be negative.";
        if (ExtendedFirstInstalmentBeforeHire < ExtendedFinalInstalmentBeforeHire)
            yield return "Notifications__ExtendedFirstInstalmentBeforeHire must be at least ExtendedFinalInstalmentBeforeHire.";
    }
}

/// <summary>Hold and draft timing (§10.2). Bound from the "Booking" section.</summary>
public sealed class BookingOptions
{
    public const string Section = "Booking";
    /// <summary>Stripe's minimum Checkout session lifetime.</summary>
    public static readonly TimeSpan StripeMinimumSession = TimeSpan.FromMinutes(30);

    public TimeSpan PendingHoldDuration { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan PendingHoldGrace { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan AbandonedDraftAfter { get; set; } = TimeSpan.FromHours(2);
    public TimeSpan DraftRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How long the Stripe session lives: max(PendingHoldDuration, 30 min).</summary>
    public TimeSpan CheckoutSessionLifetime => PendingHoldDuration < StripeMinimumSession ? StripeMinimumSession : PendingHoldDuration;
    /// <summary>The hold: session lifetime + grace.</summary>
    public TimeSpan HoldDuration => CheckoutSessionLifetime + PendingHoldGrace;

    public IEnumerable<string> Validate()
    {
        if (PendingHoldDuration <= TimeSpan.Zero || PendingHoldDuration > TimeSpan.FromHours(23))
            yield return "Booking__PendingHoldDuration must be between 0 and 23 hours (Stripe sessions last at most 24 hours).";
        if (PendingHoldGrace < TimeSpan.Zero) yield return "Booking__PendingHoldGrace can't be negative.";
        if (AbandonedDraftAfter <= TimeSpan.Zero) yield return "Booking__AbandonedDraftAfter must be positive.";
        if (DraftRetention <= TimeSpan.Zero) yield return "Booking__DraftRetention must be positive.";
    }
}

/// <summary>Where customer-facing links point.</summary>
public sealed class AppUrlOptions
{
    /// <summary>e.g. https://bookings.bergerhouseboats.com.au (no trailing slash). Required; no default.</summary>
    public string PublicBookingBaseUrl { get; set; } = "";

    public string BaseUrl => PublicBookingBaseUrl.TrimEnd('/');
    public string PaymentLink(string token) => $"{BaseUrl}/pay/?token={token}";
}

/// <summary>
/// Payment-link tokens: 32 bytes (HMAC-SHA256 of the booking id under a server secret), base64url. Only the SHA-256 hash is stored.
/// Deriving the token keeps it stable for the booking's life while letting later emails include it.
/// </summary>
public sealed class PaymentLinkTokens(string secret)
{
    public string TokenFor(string bookingId)
    {
        var bytes = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes("payment-link:" + bookingId));
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public string HashFor(string bookingId) => Hash(TokenFor(bookingId));
}

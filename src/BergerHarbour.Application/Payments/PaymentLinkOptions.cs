namespace BergerHarbour.Application.Payments;

/// <summary>Where payment links point, and the secret their tokens are derived with.</summary>
public sealed class PaymentLinkOptions
{
    /// <summary>The booking app's base URL, e.g. https://bookings.bergerhouseboats.com.au (no default).</summary>
    public string PublicBookingBaseUrl { get; set; } = string.Empty;

    /// <summary>At least 32 characters; tokens are HMAC-SHA256 of the booking id, so they are stable for the booking's life.</summary>
    public string Secret { get; set; } = string.Empty;
}

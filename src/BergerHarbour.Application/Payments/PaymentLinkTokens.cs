using System.Security.Cryptography;
using System.Text;
using BergerHarbour.Domain.Bookings;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Application.Payments;

/// <summary>
/// Payment link tokens: 32 bytes, base64url, derived from the booking id with a secret so the same link can be emailed
/// again for the booking's whole life. Only the SHA-256 hash is stored on the booking.
/// </summary>
public sealed class PaymentLinkTokens
{
    private readonly PaymentLinkOptions options;

    public PaymentLinkTokens(IOptions<PaymentLinkOptions> options)
    {
        this.options = options.Value;
    }

    public string BaseUrl => options.PublicBookingBaseUrl.TrimEnd('/');

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public string TokenFor(BookingId bookingId)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Secret), Encoding.UTF8.GetBytes("payment-link:" + bookingId.Value));
        return Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public string HashFor(BookingId bookingId) => Hash(TokenFor(bookingId));

    public string LinkFor(BookingId bookingId) => $"{BaseUrl}/pay/?token={TokenFor(bookingId)}";
}

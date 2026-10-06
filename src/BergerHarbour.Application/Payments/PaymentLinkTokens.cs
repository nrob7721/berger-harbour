using System.Security.Cryptography;
using System.Text;
using BergerHarbour.Application.Common;
using BergerHarbour.Domain.Shared;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Application.Payments;

/// <summary>
/// Payment-link tokens. Each booking's token is 32 bytes, base64url, derived as HMAC-SHA256(PAYMENT_LINK_SECRET,
/// bookingId) so it is unguessable, stable for the booking's life and can be rebuilt when a reminder is emailed.
/// Only its SHA-256 hash is stored on the booking.
/// </summary>
public sealed class PaymentLinkTokens(IOptions<PaymentLinkOptions> options)
{
    public string TokenFor(BookingId bookingId)
    {
        var key = Encoding.UTF8.GetBytes(options.Value.Secret);
        var mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("payment-link:" + bookingId.Value));
        return Base64Url(mac);
    }

    public string HashFor(BookingId bookingId) => Hash(TokenFor(bookingId));

    public string LinkFor(BookingId bookingId) =>
        $"{options.Value.PublicBookingBaseUrl.TrimEnd('/')}/pay/?token={TokenFor(bookingId)}";

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

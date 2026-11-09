using System.Security.Cryptography;

namespace BergerHarbour.Domain.BookingDrafts;

/// <summary>Typed id of a booking draft: random and unguessable (192 bits, base64url).</summary>
public readonly record struct BookingDraftId(string Value)
{
    public static BookingDraftId New() =>
        new(Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_'));

    public override string ToString() => Value;
}

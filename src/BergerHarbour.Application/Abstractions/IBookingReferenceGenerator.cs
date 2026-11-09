namespace BergerHarbour.Application.Abstractions;

/// <summary>The booking reference suffix: how many references already have the prefix, from a transactional counter.</summary>
public interface IBookingReferenceGenerator
{
    /// <summary>Returns 0 for the first reference with this <c>LASTNAME-YYMMDD</c> prefix, then 1, 2, …</summary>
    Task<int> NextSuffixAsync(string prefix, CancellationToken ct = default);
}

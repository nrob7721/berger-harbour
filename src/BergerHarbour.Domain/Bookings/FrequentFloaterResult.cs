namespace BergerHarbour.Domain.Bookings;

/// <summary>Whether a booking is a Frequent Floater booking, and how many other bookings make it one.</summary>
public sealed record FrequentFloaterResult(bool IsFrequentFloater, int OtherBookingCount)
{
    public static readonly FrequentFloaterResult No = new(false, 0);
}

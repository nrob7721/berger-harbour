namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>Whether the booking is a Frequent Floater booking (§7.8).</summary>
public sealed record FrequentFloaterDto(
    bool IsFrequentFloater,
    int OtherBookingCount);

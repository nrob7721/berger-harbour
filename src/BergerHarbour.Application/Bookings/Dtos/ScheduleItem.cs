using BergerHarbour.Domain.Bookings;

namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>A payment milestone for the customer.</summary>
public sealed record ScheduleItem(
    MilestoneKind Kind,
    string Label,
    decimal RequiredCumulative,
    decimal Amount,
    DateOnly DueDate,
    bool DueAtBooking);

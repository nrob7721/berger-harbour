using BergerHarbour.Domain.Bookings;

namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>A payment milestone with its current shortfall.</summary>
public sealed record AdminScheduleItem(
    MilestoneKind Kind,
    string Label,
    decimal RequiredCumulative,
    DateOnly DueDate,
    decimal Shortfall);

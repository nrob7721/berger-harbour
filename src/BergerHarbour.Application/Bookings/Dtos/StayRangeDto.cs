namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>A stay <c>[start, end)</c>.</summary>
public sealed record StayRangeDto(
    DateOnly Start,
    DateOnly End);

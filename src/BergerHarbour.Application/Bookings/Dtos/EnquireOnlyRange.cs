namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>A blocked period: customers must phone or email to book it.</summary>
public sealed record EnquireOnlyRange(
    string Name,
    DateOnly FirstNight,
    DateOnly LastNight);

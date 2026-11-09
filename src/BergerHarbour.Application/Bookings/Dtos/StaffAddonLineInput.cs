namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>A booking's add-on line as staff save it: no id means a new line.</summary>
public sealed record StaffAddonLineInput(
    string? Id,
    string AddonId,
    int Quantity,
    decimal? UnitPrice);

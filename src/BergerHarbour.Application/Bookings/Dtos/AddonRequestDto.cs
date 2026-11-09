namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>An add-on the customer asks for.</summary>
public sealed record AddonRequestDto(
    string AddonId,
    int Quantity);

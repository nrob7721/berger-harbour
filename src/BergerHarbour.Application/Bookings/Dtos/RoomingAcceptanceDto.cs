namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>When the rooming warning was accepted, and the text shown.</summary>
public sealed record RoomingAcceptanceDto(
    DateTimeOffset AcceptedAt,
    string WarningTextShown);

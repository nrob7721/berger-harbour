namespace BergerHarbour.Domain.Bookings;

/// <summary>The customer accepted the rooming warning: when, and the exact text shown.</summary>
public sealed record RoomingWarningAcceptance(DateTimeOffset AcceptedAt, string WarningTextShown);

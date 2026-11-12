namespace BergerHarbour.Application.BookingDrafts.Dtos;

/// <summary>The new draft's id, kept in the booking app's saved progress.</summary>
public sealed record CreateDraftResponse(
    string DraftId);

namespace BergerHarbour.Application.BookingDrafts.Dtos;

/// <summary>The new draft's id, kept in sessionStorage.</summary>
public sealed record CreateDraftResponse(
    string DraftId);

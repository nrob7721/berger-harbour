namespace BergerHarbour.Application.BookingDrafts.Dtos;

/// <summary>A draft's personal details, read back when the booking app resumes its saved progress (§11.2).</summary>
public sealed record DraftDetailsResponse(
    string FirstName,
    string LastName,
    string Email,
    string Mobile);

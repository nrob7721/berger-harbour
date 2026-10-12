using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.BookingDrafts;

public interface IBookingDraftRepository
{
    Task<BookingDraft?> GetAsync(BookingDraftId id, CancellationToken ct = default);

    /// <summary>Every draft with the normalised email.</summary>
    Task<IReadOnlyList<BookingDraft>> ListByEmailAsync(EmailAddress email, CancellationToken ct = default);

    /// <summary>Drafts whose last activity was before the instant (converted or not).</summary>
    Task<IReadOnlyList<BookingDraft>> ListInactiveSinceAsync(DateTimeOffset lastActivityBefore, CancellationToken ct = default);

    /// <summary>Drafts created before the instant.</summary>
    Task<IReadOnlyList<BookingDraft>> ListCreatedBeforeAsync(DateTimeOffset createdBefore, CancellationToken ct = default);

    /// <summary>Inserts a new draft (Version 0) or updates one with an optimistic version check.</summary>
    Task SaveAsync(BookingDraft draft, CancellationToken ct = default);

    Task DeleteAsync(BookingDraft draft, CancellationToken ct = default);
}

using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.BookingDrafts;

public interface IBookingDraftRepository
{
    Task<BookingDraft?> GetAsync(BookingDraftId id, CancellationToken ct = default);

    Task SaveAsync(BookingDraft draft, CancellationToken ct = default);

    /// <summary>Unconverted drafts not yet reported as abandoned whose last activity is before <paramref name="cutoff"/>.</summary>
    Task<IReadOnlyList<BookingDraft>> ListAbandonCandidatesAsync(DateTimeOffset cutoff, CancellationToken ct = default);

    Task<IReadOnlyList<BookingDraft>> ListByEmailAsync(EmailAddress email, CancellationToken ct = default);

    Task<IReadOnlyList<BookingDraft>> ListCreatedBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default);

    Task DeleteAsync(BookingDraftId id, CancellationToken ct = default);
}

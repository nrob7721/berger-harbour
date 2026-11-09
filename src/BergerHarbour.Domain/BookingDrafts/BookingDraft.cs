using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.BookingDrafts;

/// <summary>
/// The details a customer entered online before paying. Not a customer record: if it isn't converted into an Active
/// booking, staff are alerted (email #10).
/// </summary>
public sealed class BookingDraft : AggregateRoot
{
    private BookingDraft(BookingDraftId id, DraftContents contents, DateTimeOffset createdDate)
    {
        Id = id;
        Contents = contents;
        CreatedDate = createdDate;
        LastActivityAt = createdDate;
    }

    public BookingDraftId Id { get; }

    public DraftContents Contents { get; private set; }

    public DateTimeOffset LastActivityAt { get; private set; }

    public BookingId? ConvertedBookingId { get; private set; }

    public DateTimeOffset? ConvertedAt { get; private set; }

    public DateTimeOffset? AbandonedNotifiedAt { get; private set; }

    public DateTimeOffset CreatedDate { get; }

    public bool IsConverted => ConvertedBookingId is not null;

    public static BookingDraft Create(BookingDraftId id, DraftContents contents, DateTimeOffset now)
    {
        Validate(contents);
        return new BookingDraft(id, contents, now);
    }

    public static BookingDraft Rehydrate(
        BookingDraftId id, DraftContents contents, DateTimeOffset lastActivityAt, BookingId? convertedBookingId, DateTimeOffset? convertedAt,
        DateTimeOffset? abandonedNotifiedAt, DateTimeOffset createdDate, int version)
    {
        var draft = new BookingDraft(id, contents, createdDate)
        {
            LastActivityAt = lastActivityAt,
            ConvertedBookingId = convertedBookingId,
            ConvertedAt = convertedAt,
            AbandonedNotifiedAt = abandonedNotifiedAt,
        };
        draft.SetPersistedVersion(version);
        return draft;
    }

    /// <summary>A converted draft can no longer be updated.</summary>
    public void Update(DraftContents contents, DateTimeOffset now)
    {
        if (IsConverted)
        {
            throw new NotFoundException("This booking has already been completed.");
        }

        Validate(contents);
        Contents = contents;
        LastActivityAt = now;
    }

    public void MarkConverted(BookingId bookingId, DateTimeOffset now)
    {
        if (ConvertedBookingId is null)
        {
            ConvertedBookingId = bookingId;
            ConvertedAt = now;
        }
    }

    /// <summary>Not converted and inactive for longer than <paramref name="abandonedAfter"/>.</summary>
    public bool IsAbandoned(DateTimeOffset now, TimeSpan abandonedAfter) => !IsConverted && now > LastActivityAt + abandonedAfter;

    public bool IsPastRetention(DateTimeOffset now, TimeSpan retention) => now > CreatedDate + retention;

    public void MarkAbandonedNotified(DateTimeOffset now) => AbandonedNotifiedAt ??= now;

    private static void Validate(DraftContents contents)
    {
        var errors = new ValidationErrors();
        if (contents.NumberOfAdults < 1)
        {
            errors.Add("numberOfAdults", "At least 1 adult is required.");
        }

        if (contents.NumberOfChildren < 0)
        {
            errors.Add("numberOfChildren", "Children can't be negative.");
        }

        if (string.IsNullOrWhiteSpace(contents.FirstName))
        {
            errors.Add("firstName", "First name is required.");
        }

        if (string.IsNullOrWhiteSpace(contents.LastName))
        {
            errors.Add("lastName", "Last name is required.");
        }

        if (contents.AddonRequests.Any(a => a.Quantity < 1))
        {
            errors.Add("addons", "Each add-on needs a quantity of at least 1.");
        }

        errors.ThrowIfAny();
    }
}

using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.BookingDrafts;

/// <summary>The booking app steps a draft can reach, in order.</summary>
public enum BookingStep
{
    Details,
    Guests,
    Addons,
    Summary,
    Payment,
}

public sealed record DraftAddonRequest(AddonId AddonId, int Quantity);

/// <summary>
/// What a customer has entered in the booking app (from "Your details" on) before paying. It is not a customer
/// record: if it is never converted into an Active booking, staff are alerted so they can follow up.
/// </summary>
public sealed class BookingDraft : AggregateRoot<BookingDraftId>
{
    private List<DraftAddonRequest> _addonRequests;

    private BookingDraft(BookingDraftId id, int version, BoatId boatId, PeriodType periodType, Stay stay, string fullName,
        EmailAddress email, string mobileNumber, int? numberOfAdults, int? numberOfChildren,
        IEnumerable<DraftAddonRequest> addonRequests, BookingStep lastStepReached, DateTimeOffset lastActivityAt,
        BookingId? convertedBookingId, DateTimeOffset? convertedAt, DateTimeOffset? abandonedNotifiedAt,
        DateTimeOffset createdDate)
        : base(id, version)
    {
        BoatId = boatId;
        PeriodType = periodType;
        Stay = stay;
        FullName = fullName;
        Email = email;
        MobileNumber = mobileNumber;
        NumberOfAdults = numberOfAdults;
        NumberOfChildren = numberOfChildren;
        _addonRequests = addonRequests.ToList();
        LastStepReached = lastStepReached;
        LastActivityAt = lastActivityAt;
        ConvertedBookingId = convertedBookingId;
        ConvertedAt = convertedAt;
        AbandonedNotifiedAt = abandonedNotifiedAt;
        CreatedDate = createdDate;
    }

    public BoatId BoatId { get; }

    public PeriodType PeriodType { get; private set; }

    public Stay Stay { get; private set; }

    public string FullName { get; private set; }

    public EmailAddress Email { get; private set; }

    /// <summary>E.164.</summary>
    public string MobileNumber { get; private set; }

    public int? NumberOfAdults { get; private set; }

    public int? NumberOfChildren { get; private set; }

    public IReadOnlyList<DraftAddonRequest> AddonRequests => _addonRequests;

    /// <summary>The furthest step the customer reached.</summary>
    public BookingStep LastStepReached { get; private set; }

    public DateTimeOffset LastActivityAt { get; private set; }

    public BookingId? ConvertedBookingId { get; private set; }

    public DateTimeOffset? ConvertedAt { get; private set; }

    /// <summary>When the staff alert (#10) was sent.</summary>
    public DateTimeOffset? AbandonedNotifiedAt { get; private set; }

    public DateTimeOffset CreatedDate { get; }

    public bool IsConverted => ConvertedBookingId is not null;

    public static BookingDraft Create(BoatId boatId, PeriodType periodType, Stay stay, string fullName, string email,
        string mobileNumber, DateTimeOffset now, BookingDraftId? id = null) =>
        new(id ?? BookingDraftId.New(), 0, boatId, periodType, stay, Guard.Required(fullName, "fullName", "Full name"),
            EmailAddress.Create(email), PhoneNumber.Normalise(mobileNumber, "mobile", "Mobile number"), null, null, [],
            BookingStep.Details, now, null, null, null, now);

    /// <summary>The customer moved forward a step. A converted draft can no longer be updated.</summary>
    public void Update(PeriodType periodType, Stay stay, string fullName, string email, string mobileNumber,
        int? numberOfAdults, int? numberOfChildren, IEnumerable<DraftAddonRequest> addonRequests, BookingStep stepReached,
        DateTimeOffset now)
    {
        EnsureNotConverted();
        if (numberOfAdults is < 1)
        {
            throw new DomainValidationException("numberOfAdults", "There must be at least one adult.");
        }

        if (numberOfChildren is < 0)
        {
            throw new DomainValidationException("numberOfChildren", "Number of children cannot be negative.");
        }

        PeriodType = periodType;
        Stay = stay;
        FullName = Guard.Required(fullName, "fullName", "Full name");
        Email = EmailAddress.Create(email);
        MobileNumber = PhoneNumber.Normalise(mobileNumber, "mobile", "Mobile number");
        NumberOfAdults = numberOfAdults;
        NumberOfChildren = numberOfChildren;
        _addonRequests = addonRequests.Where(a => a.Quantity >= 1).ToList();
        LastStepReached = stepReached > LastStepReached ? stepReached : LastStepReached;
        LastActivityAt = now;
    }

    /// <summary>Its booking became Active. Idempotent for the same booking.</summary>
    public void MarkConverted(BookingId bookingId, DateTimeOffset now)
    {
        if (ConvertedBookingId == bookingId)
        {
            return;
        }

        EnsureNotConverted();
        ConvertedBookingId = bookingId;
        ConvertedAt = now;
    }

    public void MarkAbandonedNotified(DateTimeOffset now) => AbandonedNotifiedAt ??= now;

    /// <summary>Not converted and inactive for longer than <paramref name="abandonedAfter"/>.</summary>
    public bool IsAbandoned(DateTimeOffset now, TimeSpan abandonedAfter) =>
        !IsConverted && now > LastActivityAt + abandonedAfter;

    /// <summary>Drafts are deleted after the retention period, whether converted or not.</summary>
    public bool IsPastRetention(DateTimeOffset now, TimeSpan retention) => now > CreatedDate + retention;

    public BookingDraftSnapshot ToSnapshot() => new(
        Id.Value, Version, BoatId.Value, PeriodType, Stay.StartDate, Stay.EndDate, FullName, Email.Value, MobileNumber,
        NumberOfAdults, NumberOfChildren, _addonRequests.Select(a => new DraftAddonRequestSnapshot(a.AddonId.Value, a.Quantity)).ToList(),
        LastStepReached, LastActivityAt, ConvertedBookingId?.Value, ConvertedAt, AbandonedNotifiedAt, CreatedDate);

    public static BookingDraft FromSnapshot(BookingDraftSnapshot s) => new(
        new BookingDraftId(s.Id), s.Version, new BoatId(s.BoatId), s.PeriodType, new Stay(s.StartDate, s.EndDate),
        s.FullName, EmailAddress.Create(s.Email), s.MobileNumber, s.NumberOfAdults, s.NumberOfChildren,
        s.AddonRequests.Select(a => new DraftAddonRequest(new AddonId(a.AddonId), a.Quantity)), s.LastStepReached,
        s.LastActivityAt, string.IsNullOrEmpty(s.ConvertedBookingId) ? null : new BookingId(s.ConvertedBookingId),
        s.ConvertedAt, s.AbandonedNotifiedAt, s.CreatedDate);

    private void EnsureNotConverted()
    {
        if (IsConverted)
        {
            throw new InvalidStateTransitionException("This booking has already been completed.");
        }
    }
}

/// <summary>When staff are told about an abandoned draft (email #10).</summary>
public static class AbandonedDraftPolicy
{
    /// <summary>
    /// True when #10 should be sent for the draft: it is abandoned and not yet notified, it is the latest draft for
    /// its email (by last activity), and no booking for that email has become Active since the draft was created.
    /// </summary>
    /// <param name="draftsForEmail">Every draft with the same normalised email, including this one.</param>
    /// <param name="bookingsForEmail">Every booking of the customer with that email.</param>
    public static bool ShouldNotify(BookingDraft draft, IEnumerable<BookingDraft> draftsForEmail,
        IEnumerable<Booking> bookingsForEmail, DateTimeOffset now, TimeSpan abandonedAfter)
    {
        if (draft.AbandonedNotifiedAt is not null || !draft.IsAbandoned(now, abandonedAfter))
        {
            return false;
        }

        var latest = draftsForEmail.Append(draft)
            .OrderByDescending(d => d.LastActivityAt)
            .ThenByDescending(d => d.CreatedDate)
            .ThenBy(d => d.Id.Value, StringComparer.Ordinal)
            .First();
        if (latest.Id != draft.Id)
        {
            return false;
        }

        return !bookingsForEmail.Any(b => b.HasBeenActive && b.CreatedDate >= draft.CreatedDate);
    }
}

public sealed record DraftAddonRequestSnapshot(string AddonId, int Quantity);

public sealed record BookingDraftSnapshot(
    string Id,
    int Version,
    string BoatId,
    PeriodType PeriodType,
    DateOnly StartDate,
    DateOnly EndDate,
    string FullName,
    string Email,
    string MobileNumber,
    int? NumberOfAdults,
    int? NumberOfChildren,
    IReadOnlyList<DraftAddonRequestSnapshot> AddonRequests,
    BookingStep LastStepReached,
    DateTimeOffset LastActivityAt,
    string? ConvertedBookingId,
    DateTimeOffset? ConvertedAt,
    DateTimeOffset? AbandonedNotifiedAt,
    DateTimeOffset CreatedDate);

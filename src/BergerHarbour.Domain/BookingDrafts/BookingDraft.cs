using System.Security.Cryptography;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.BookingDrafts;

public enum DraftStep { Details, Addons, Summary, Payment }

public sealed record AddonRequest(string AddonId, int Quantity);

/// <summary>What the customer entered in the booking app before paying. Never a customer record.</summary>
public sealed record DraftDetails(
    BoatId BoatId,
    PeriodType PeriodType,
    StayRange Stay,
    GuestNumbers Guests,
    string FirstName,
    string LastName,
    string Email,
    string MobileNumber,
    IReadOnlyList<AddonRequest> AddonRequests,
    DraftStep LastStepReached);

public sealed class BookingDraft : AggregateRoot
{
    public string Id { get; private set; } = "";
    public BoatId BoatId { get; private set; }
    public PeriodType PeriodType { get; private set; }
    public StayRange Stay { get; private set; }
    public GuestNumbers Guests { get; private set; } = new(1, 0);
    public string FirstName { get; private set; } = "";
    public string LastName { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string MobileNumber { get; private set; } = "";
    public IReadOnlyList<AddonRequest> AddonRequests { get; private set; } = [];
    public DraftStep LastStepReached { get; private set; }
    public DateTimeOffset LastActivityAt { get; private set; }
    public string? ConvertedBookingId { get; private set; }
    public DateTimeOffset? ConvertedAt { get; private set; }
    public DateTimeOffset? AbandonedNotifiedAt { get; private set; }

    public bool IsConverted => ConvertedAt is not null;

    private BookingDraft() { }

    /// <summary>An unguessable id (128 random bits).</summary>
    public static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public static BookingDraft Create(DraftDetails details, DateTimeOffset now)
    {
        var d = new BookingDraft { Id = NewId() };
        d.Apply(details, now);
        d.InitialiseAudit(now);
        return d;
    }

    public static BookingDraft Rehydrate(string id, DraftDetails details, DateTimeOffset lastActivityAt, string? convertedBookingId,
        DateTimeOffset? convertedAt, DateTimeOffset? abandonedNotifiedAt, int version, DateTimeOffset created, DateTimeOffset modified)
    {
        var d = new BookingDraft
        {
            Id = id, BoatId = details.BoatId, PeriodType = details.PeriodType, Stay = details.Stay, Guests = details.Guests,
            FirstName = details.FirstName, LastName = details.LastName, Email = details.Email, MobileNumber = details.MobileNumber,
            AddonRequests = details.AddonRequests, LastStepReached = details.LastStepReached, LastActivityAt = lastActivityAt,
            ConvertedBookingId = convertedBookingId, ConvertedAt = convertedAt, AbandonedNotifiedAt = abandonedNotifiedAt,
        };
        d.RestoreAudit(version, created, modified);
        return d;
    }

    public void Update(DraftDetails details, DateTimeOffset now)
    {
        if (IsConverted) throw new NotFoundException("This booking has already been completed.");
        Apply(details, now);
    }

    private void Apply(DraftDetails d, DateTimeOffset now)
    {
        var errors = new ValidationErrors();
        var first = errors.Capture(() => Text.Required(d.FirstName, "firstName", "First name", 100));
        var last = errors.Capture(() => Text.Required(d.LastName, "lastName", "Last name", 100));
        var email = errors.Capture(() => EmailAddress.Require(d.Email));
        var mobile = errors.Capture(() => Shared.MobileNumber.Require(d.MobileNumber, "mobile"));
        errors.ThrowIfAny();
        BoatId = d.BoatId;
        PeriodType = d.PeriodType;
        Stay = d.Stay;
        Guests = d.Guests;
        FirstName = first!;
        LastName = last!;
        Email = email!;
        MobileNumber = mobile!;
        AddonRequests = d.AddonRequests.ToList();
        // The furthest step reached never goes backwards.
        if (LastActivityAt == default || d.LastStepReached > LastStepReached) LastStepReached = d.LastStepReached;
        LastActivityAt = now;
    }

    public void MarkConverted(string bookingId, DateTimeOffset now)
    {
        if (IsConverted) return;
        ConvertedBookingId = bookingId;
        ConvertedAt = now;
    }

    public bool IsAbandoned(DateTimeOffset now, TimeSpan abandonedAfter) => !IsConverted && now > LastActivityAt + abandonedAfter;

    public bool IsPastRetention(DateTimeOffset now, TimeSpan retention) => now >= CreatedDate + retention;

    public void MarkAbandonedNotified(DateTimeOffset now) => AbandonedNotifiedAt ??= now;
}

public interface IBookingDraftRepository
{
    Task<BookingDraft?> GetAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<BookingDraft>> ListAsync(CancellationToken ct = default);
    /// <summary>The draft with the latest activity for that normalised email.</summary>
    Task<BookingDraft?> GetLatestForEmailAsync(string normalisedEmail, CancellationToken ct = default);
    Task SaveAsync(BookingDraft draft, CancellationToken ct = default);
    Task DeleteAsync(BookingDraft draft, CancellationToken ct = default);
}

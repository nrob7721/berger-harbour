using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

[FirestoreData]
public sealed class BookingDraftDocument : IVersionedDocument
{
    [FirestoreProperty("boatId")]
    public string BoatId { get; set; } = string.Empty;

    [FirestoreProperty("periodType")]
    public string PeriodType { get; set; } = string.Empty;

    [FirestoreProperty("startDate")]
    public string StartDate { get; set; } = string.Empty;

    [FirestoreProperty("endDate")]
    public string EndDate { get; set; } = string.Empty;

    [FirestoreProperty("numberOfAdults")]
    public int NumberOfAdults { get; set; }

    [FirestoreProperty("numberOfChildren")]
    public int NumberOfChildren { get; set; }

    [FirestoreProperty("firstName")]
    public string FirstName { get; set; } = string.Empty;

    [FirestoreProperty("lastName")]
    public string LastName { get; set; } = string.Empty;

    [FirestoreProperty("email")]
    public string Email { get; set; } = string.Empty;

    [FirestoreProperty("mobileNumber")]
    public string MobileNumber { get; set; } = string.Empty;

    [FirestoreProperty("addonRequests")]
    public List<AddonRequestDocument> AddonRequests { get; set; } = [];

    [FirestoreProperty("lastStepReached")]
    public string LastStepReached { get; set; } = string.Empty;

    [FirestoreProperty("lastActivityAt")]
    public Timestamp LastActivityAt { get; set; }

    [FirestoreProperty("convertedBookingId")]
    public string? ConvertedBookingId { get; set; }

    [FirestoreProperty("convertedAt")]
    public Timestamp? ConvertedAt { get; set; }

    [FirestoreProperty("abandonedNotifiedAt")]
    public Timestamp? AbandonedNotifiedAt { get; set; }

    [FirestoreProperty("createdDate")]
    public Timestamp CreatedDate { get; set; }

    [FirestoreProperty("version")]
    public int Version { get; set; }

    public static BookingDraftDocument From(BookingDraft d) => new()
    {
        BoatId = d.Contents.BoatId.Value,
        PeriodType = d.Contents.PeriodType.ToString(),
        StartDate = FirestoreValues.Date(d.Contents.Stay.Start),
        EndDate = FirestoreValues.Date(d.Contents.Stay.End),
        NumberOfAdults = d.Contents.NumberOfAdults,
        NumberOfChildren = d.Contents.NumberOfChildren,
        FirstName = d.Contents.FirstName,
        LastName = d.Contents.LastName,
        Email = d.Contents.Email.Value,
        MobileNumber = d.Contents.MobileNumber.Value,
        AddonRequests = d.Contents.AddonRequests.Select(AddonRequestDocument.From).ToList(),
        LastStepReached = d.Contents.LastStepReached.ToString(),
        LastActivityAt = FirestoreValues.Instant(d.LastActivityAt),
        ConvertedBookingId = d.ConvertedBookingId?.Value,
        ConvertedAt = FirestoreValues.Instant(d.ConvertedAt),
        AbandonedNotifiedAt = FirestoreValues.Instant(d.AbandonedNotifiedAt),
        CreatedDate = FirestoreValues.Instant(d.CreatedDate),
        Version = d.Version,
    };

    public BookingDraft ToDomain(string id) => BookingDraft.Rehydrate(
        new BookingDraftId(id),
        new DraftContents(new BoatId(BoatId), Enum.Parse<PeriodType>(PeriodType),
            StayRange.Create(FirestoreValues.Date(StartDate), FirestoreValues.Date(EndDate)), NumberOfAdults, NumberOfChildren, FirstName, LastName,
            EmailAddress.Create(Email), Domain.Shared.MobileNumber.Create(MobileNumber), AddonRequests.Select(a => a.ToDomain()).ToList(),
            Enum.Parse<DraftStep>(LastStepReached)),
        FirestoreValues.Instant(LastActivityAt),
        ConvertedBookingId is null ? null : new BookingId(ConvertedBookingId),
        FirestoreValues.Instant(ConvertedAt),
        FirestoreValues.Instant(AbandonedNotifiedAt),
        FirestoreValues.Instant(CreatedDate),
        Version);
}

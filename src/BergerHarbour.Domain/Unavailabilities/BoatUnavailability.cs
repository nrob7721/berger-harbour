using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Unavailabilities;

/// <summary>A range of nights when a specific boat cannot be hired (maintenance, etc.).</summary>
public sealed class BoatUnavailability : AggregateRoot<UnavailabilityId>
{
    private BoatUnavailability(UnavailabilityId id, int version, BoatId boatId, NightRange nights, string? comments,
        DateTimeOffset createdDate, DateTimeOffset modifiedDate)
        : base(id, version)
    {
        BoatId = boatId;
        Nights = nights;
        Comments = comments;
        CreatedDate = createdDate;
        ModifiedDate = modifiedDate;
    }

    public BoatId BoatId { get; }

    public NightRange Nights { get; private set; }

    public string? Comments { get; private set; }

    public DateTimeOffset CreatedDate { get; }

    public DateTimeOffset ModifiedDate { get; private set; }

    public static BoatUnavailability Create(BoatId boatId, DateOnly firstNight, DateOnly lastNight, string? comments,
        DateTimeOffset now) =>
        new(UnavailabilityId.New(), 0, boatId, new NightRange(firstNight, lastNight),
            Guard.Optional(comments, "comments", "Comments"), now, now);

    public void Update(DateOnly firstNight, DateOnly lastNight, string? comments, DateTimeOffset now)
    {
        Nights = new NightRange(firstNight, lastNight);
        Comments = Guard.Optional(comments, "comments", "Comments");
        ModifiedDate = now;
    }

    public BoatUnavailabilitySnapshot ToSnapshot() =>
        new(Id.Value, Version, BoatId.Value, Nights.FirstNight, Nights.LastNight, Comments, CreatedDate, ModifiedDate);

    public static BoatUnavailability FromSnapshot(BoatUnavailabilitySnapshot s) =>
        new(new UnavailabilityId(s.Id), s.Version, new BoatId(s.BoatId), new NightRange(s.FirstNight, s.LastNight),
            s.Comments, s.CreatedDate, s.ModifiedDate);
}

public sealed record BoatUnavailabilitySnapshot(
    string Id,
    int Version,
    string BoatId,
    DateOnly FirstNight,
    DateOnly LastNight,
    string? Comments,
    DateTimeOffset CreatedDate,
    DateTimeOffset ModifiedDate);

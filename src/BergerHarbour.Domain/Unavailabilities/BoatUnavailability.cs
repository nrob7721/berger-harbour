using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Unavailabilities;

/// <summary>Nights a specific boat can't be hired (maintenance etc.). Inclusive nights.</summary>
public sealed class BoatUnavailability : AggregateRoot
{
    private BoatUnavailability(BoatUnavailabilityId id, BoatId boatId, NightRange nights, string? comments, DateTimeOffset createdDate)
    {
        Id = id;
        BoatId = boatId;
        Nights = nights;
        Comments = comments;
        CreatedDate = createdDate;
        ModifiedDate = createdDate;
    }

    public BoatUnavailabilityId Id { get; }

    public BoatId BoatId { get; }

    public NightRange Nights { get; private set; }

    public string? Comments { get; private set; }

    public DateTimeOffset CreatedDate { get; }

    public DateTimeOffset ModifiedDate { get; private set; }

    public static BoatUnavailability Create(BoatUnavailabilityId id, BoatId boatId, NightRange nights, string? comments, DateTimeOffset now) =>
        new(id, boatId, nights, Normalise(comments), now);

    public static BoatUnavailability Rehydrate(
        BoatUnavailabilityId id, BoatId boatId, NightRange nights, string? comments, DateTimeOffset createdDate, DateTimeOffset modifiedDate,
        int version)
    {
        var unavailability = new BoatUnavailability(id, boatId, nights, comments, createdDate) { ModifiedDate = modifiedDate };
        unavailability.SetPersistedVersion(version);
        return unavailability;
    }

    public void Update(NightRange nights, string? comments, DateTimeOffset now)
    {
        Nights = nights;
        Comments = Normalise(comments);
        ModifiedDate = now;
    }

    private static string? Normalise(string? comments)
    {
        var trimmed = string.IsNullOrWhiteSpace(comments) ? null : comments.Trim();
        return trimmed?.Length > 1000 ? throw new DomainValidationException("comments", "Comments must be 1000 characters or fewer.") : trimmed;
    }
}

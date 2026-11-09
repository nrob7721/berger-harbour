using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Unavailabilities;

/// <summary>A range of nights when a boat can't be hired (maintenance, etc.).</summary>
public sealed class BoatUnavailability : AggregateRoot
{
    public string Id { get; private set; } = "";
    public BoatId BoatId { get; private set; }
    public NightRange Nights { get; private set; }
    public string? Comments { get; private set; }

    private BoatUnavailability() { }

    public static BoatUnavailability Create(BoatId boatId, DateOnly firstNight, DateOnly lastNight, string? comments, DateTimeOffset now)
    {
        var u = new BoatUnavailability { Id = Ids.New(), BoatId = boatId };
        u.Update(firstNight, lastNight, comments);
        u.InitialiseAudit(now);
        return u;
    }

    public static BoatUnavailability Rehydrate(string id, BoatId boatId, NightRange nights, string? comments, int version, DateTimeOffset created, DateTimeOffset modified)
    {
        var u = new BoatUnavailability { Id = id, BoatId = boatId, Nights = nights, Comments = comments };
        u.RestoreAudit(version, created, modified);
        return u;
    }

    public void Update(DateOnly firstNight, DateOnly lastNight, string? comments)
    {
        var errors = new ValidationErrors();
        var nights = errors.Capture(() => (NightRange?)new NightRange(firstNight, lastNight));
        var c = errors.Capture(() => Text.Optional(comments, "comments", "Comments", 1000));
        errors.ThrowIfAny();
        Nights = nights!.Value;
        Comments = c;
    }

    /// <summary>The same nights as a booking stay, for overlap checks: [FirstNight, LastNight + 1).</summary>
    public StayRange AsStay => Nights.AsStay();
}

public interface IBoatUnavailabilityRepository
{
    Task<BoatUnavailability?> GetAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<BoatUnavailability>> ListForBoatAsync(BoatId boatId, CancellationToken ct = default);
    /// <summary>Unavailabilities of any boat with a night in [from, to).</summary>
    Task<IReadOnlyList<BoatUnavailability>> ListInRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task DeleteAsync(BoatUnavailability unavailability, CancellationToken ct = default);
}

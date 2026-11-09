using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Settings;

/// <summary>A named recurring pricing season. Exactly one built-in default season (Normal) has no ranges.</summary>
public sealed class Season : AggregateRoot
{
    public const string DefaultName = "Normal";

    private Season(SeasonId id, string name, bool isDefault, IReadOnlyList<SeasonRange> ranges)
    {
        Id = id;
        Name = name;
        IsDefault = isDefault;
        Ranges = ranges;
    }

    public SeasonId Id { get; }

    public string Name { get; private set; }

    public bool IsDefault { get; }

    public IReadOnlyList<SeasonRange> Ranges { get; private set; }

    public static Season CreateDefault(SeasonId id) => new(id, DefaultName, true, []);

    /// <summary>A new season. <paramref name="others"/> are the existing seasons its ranges must not overlap.</summary>
    public static Season Create(SeasonId id, string? name, IReadOnlyList<SeasonRange> ranges, IEnumerable<Season> others)
    {
        var season = new Season(id, ValidName(name), false, []);
        season.SetRanges(ranges, others);
        return season;
    }

    public static Season Rehydrate(SeasonId id, string name, bool isDefault, IReadOnlyList<SeasonRange> ranges, int version)
    {
        var season = new Season(id, name, isDefault, ranges);
        season.SetPersistedVersion(version);
        return season;
    }

    public void Update(string? name, IReadOnlyList<SeasonRange> ranges, IEnumerable<Season> others)
    {
        var validName = ValidName(name);
        if (IsDefault && ranges.Count > 0)
        {
            throw new DomainValidationException("ranges", "The Normal season covers every date not in another season and has no ranges.");
        }

        if (!IsDefault)
        {
            SetRanges(ranges, others);
        }

        Name = validName;
    }

    public void EnsureCanDelete()
    {
        if (IsDefault)
        {
            throw new BusinessRuleException("The Normal season can't be deleted.");
        }
    }

    public bool Contains(DateOnly date) => Ranges.Any(r => r.Contains(date));

    private static string ValidName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new DomainValidationException("name", "Name is required.");
        }

        if (trimmed.Length > 60)
        {
            throw new DomainValidationException("name", "Name must be 60 characters or fewer.");
        }

        return trimmed;
    }

    private void SetRanges(IReadOnlyList<SeasonRange> ranges, IEnumerable<Season> others)
    {
        if (ranges.Count == 0)
        {
            throw new DomainValidationException("ranges", "Add at least one date range.");
        }

        for (var i = 0; i < ranges.Count; i++)
        {
            for (var j = i + 1; j < ranges.Count; j++)
            {
                if (ranges[i].Overlaps(ranges[j]))
                {
                    throw new DomainValidationException("ranges", $"The ranges {ranges[i]} and {ranges[j]} overlap.");
                }
            }
        }

        foreach (var other in others.Where(o => o.Id != Id))
        {
            foreach (var range in ranges)
            {
                var clash = other.Ranges.FirstOrDefault(range.Overlaps);
                if (clash is not null)
                {
                    throw new DomainValidationException("ranges", $"{range} overlaps {other.Name} ({clash}).");
                }
            }
        }

        Ranges = ranges;
    }
}

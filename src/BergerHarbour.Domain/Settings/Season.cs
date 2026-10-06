using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Settings;

/// <summary>
/// A named recurring set of day-month ranges used for pricing. The built-in Normal season has no ranges and covers
/// every date that no other season covers.
/// </summary>
public sealed class Season : AggregateRoot<SeasonId>
{
    public const string NormalName = "Normal";

    private List<DayMonthRange> _ranges;

    private Season(SeasonId id, int version, string name, IEnumerable<DayMonthRange> ranges)
        : base(id, version)
    {
        Name = name;
        _ranges = ranges.ToList();
    }

    public string Name { get; private set; }

    public IReadOnlyList<DayMonthRange> Ranges => _ranges;

    public bool IsDefault => Id == SeasonId.Normal;

    public static Season CreateNormal() => new(SeasonId.Normal, 0, NormalName, []);

    public static Season Create(string name, IEnumerable<DayMonthRange> ranges) => CreateWithId(SeasonId.New(), name, ranges);

    /// <summary>Seeds use stable ids so the seed is idempotent.</summary>
    public static Season CreateWithId(SeasonId id, string name, IEnumerable<DayMonthRange> ranges)
    {
        if (string.Equals(name?.Trim(), NormalName, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainValidationException("name", "That name is reserved for the built-in Normal season.");
        }

        var season = new Season(id, 0, Guard.Required(name, "name", "Season name", 60), []);
        season.SetRanges(ranges);
        return season;
    }

    public void Update(string name, IEnumerable<DayMonthRange> ranges)
    {
        var trimmed = Guard.Required(name, "name", "Season name", 60);
        if (IsDefault)
        {
            if (trimmed != NormalName || ranges.Any())
            {
                throw new DomainValidationException("name",
                    "The built-in Normal season cannot be renamed or given ranges.");
            }

            return;
        }

        if (string.Equals(trimmed, NormalName, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainValidationException("name", "That name is reserved for the built-in Normal season.");
        }

        Name = trimmed;
        SetRanges(ranges);
    }

    public bool Contains(DateOnly date) => _ranges.Any(r => r.Contains(date));

    public void EnsureCanBeDeleted()
    {
        if (IsDefault)
        {
            throw new DomainValidationException("id", "The built-in Normal season cannot be deleted.");
        }
    }

    private void SetRanges(IEnumerable<DayMonthRange> ranges)
    {
        var list = ranges.ToList();
        if (list.Count == 0)
        {
            throw new DomainValidationException("ranges", "A season needs at least one date range.");
        }

        for (var i = 0; i < list.Count; i++)
        {
            for (var j = i + 1; j < list.Count; j++)
            {
                if (list[i].Overlaps(list[j]))
                {
                    throw new DomainValidationException("ranges", $"Ranges {list[i]} and {list[j]} overlap.");
                }
            }
        }

        _ranges = list;
    }

    public SeasonSnapshot ToSnapshot() =>
        new(Id.Value, Version, Name, _ranges.Select(r => new DayMonthRangeSnapshot(r.Start.ToString(), r.End.ToString())).ToList());

    public static Season FromSnapshot(SeasonSnapshot s) =>
        new(new SeasonId(s.Id), s.Version, s.Name,
            s.Ranges.Select(r => new DayMonthRange(DayMonth.Parse(r.StartDayMonth), DayMonth.Parse(r.EndDayMonth))));
}

public sealed record SeasonSnapshot(string Id, int Version, string Name, IReadOnlyList<DayMonthRangeSnapshot> Ranges);

public sealed record DayMonthRangeSnapshot(string StartDayMonth, string EndDayMonth);

/// <summary>Rules that span all seasons.</summary>
public static class SeasonCatalogue
{
    /// <summary>Season ranges must not overlap each other, across seasons as well as within one.</summary>
    public static void EnsureNoOverlap(Season candidate, IEnumerable<Season> others)
    {
        foreach (var other in others.Where(o => o.Id != candidate.Id))
        {
            foreach (var range in candidate.Ranges)
            {
                var clash = other.Ranges.FirstOrDefault(range.Overlaps);
                if (clash is not null)
                {
                    throw new DomainValidationException("ranges",
                        $"Range {range} overlaps {clash} in season '{other.Name}'.");
                }
            }
        }
    }
}

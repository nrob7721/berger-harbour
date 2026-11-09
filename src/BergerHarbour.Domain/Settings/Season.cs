using System.Globalization;
using System.Text.RegularExpressions;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Settings;

/// <summary>A recurring day and month, written dd-MM.</summary>
public readonly partial record struct DayMonth(int Day, int Month)
{
    [GeneratedRegex(@"^(\d{2})-(\d{2})$")]
    private static partial Regex Pattern();

    public static DayMonth Parse(string? text, string field)
    {
        var m = Pattern().Match((text ?? "").Trim());
        if (!m.Success) throw new DomainValidationException(field, "Use the format dd-MM, e.g. 01-12.");
        var day = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(2024, month))
            throw new DomainValidationException(field, $"{text} is not a valid day and month.");
        return new DayMonth(day, month);
    }

    public int Key => Month * 100 + Day;
    public override string ToString() => $"{Day:00}-{Month:00}";
}

/// <summary>A recurring range of days; it may wrap the year end (e.g. 01-12 → 31-01).</summary>
public sealed record SeasonRange(DayMonth Start, DayMonth End)
{
    public bool Wraps => End.Key < Start.Key;

    public bool Contains(DateOnly date) => Contains(date.Month * 100 + date.Day);

    private bool Contains(int key) => Wraps ? key >= Start.Key || key <= End.Key : key >= Start.Key && key <= End.Key;

    /// <summary>All day-month keys of a leap year covered by this range.</summary>
    public IEnumerable<int> Keys()
    {
        for (var d = new DateOnly(2024, 1, 1); d.Year == 2024; d = d.AddDays(1))
        {
            var key = d.Month * 100 + d.Day;
            if (Contains(key)) yield return key;
        }
    }

    public bool Overlaps(SeasonRange other) => Keys().Intersect(other.Keys()).Any();

    public override string ToString() => $"{Start} → {End}";
}

public sealed class Season : AggregateRoot
{
    public const string NormalId = "normal";

    public string Id { get; private set; } = "";
    public string Name { get; private set; } = "";
    public IReadOnlyList<SeasonRange> Ranges { get; private set; } = [];
    /// <summary>The built-in Normal season: no ranges, covers every date not in another season, can't be deleted.</summary>
    public bool IsDefault => Id == NormalId;

    private Season() { }

    public static Season CreateNormal(DateTimeOffset now)
    {
        var s = new Season { Id = NormalId, Name = "Normal" };
        s.InitialiseAudit(now);
        return s;
    }

    public static Season Create(string name, IEnumerable<SeasonRange> ranges, IEnumerable<Season> otherSeasons, DateTimeOffset now)
    {
        var s = new Season { Id = Ids.New() };
        s.Update(name, ranges, otherSeasons);
        s.InitialiseAudit(now);
        return s;
    }

    public static Season Rehydrate(string id, string name, IEnumerable<SeasonRange> ranges, int version, DateTimeOffset created, DateTimeOffset modified)
    {
        var s = new Season { Id = id, Name = name, Ranges = ranges.ToList() };
        s.RestoreAudit(version, created, modified);
        return s;
    }

    public void Update(string name, IEnumerable<SeasonRange> ranges, IEnumerable<Season> otherSeasons)
    {
        var errors = new ValidationErrors();
        var newName = errors.Capture(() => Text.Required(name, "name", "Name", 60));
        var list = ranges.ToList();
        if (IsDefault)
        {
            if (list.Count > 0) errors.Add("ranges", "The Normal season covers every date not in another season and has no ranges.");
        }
        else
        {
            if (list.Count == 0) errors.Add("ranges", "Add at least one range.");
            for (var i = 0; i < list.Count; i++)
                for (var j = i + 1; j < list.Count; j++)
                    if (list[i].Overlaps(list[j])) errors.Add("ranges", $"Ranges {list[i]} and {list[j]} overlap.");
            foreach (var other in otherSeasons.Where(o => o.Id != Id))
                foreach (var r in list)
                    foreach (var o in other.Ranges)
                        if (r.Overlaps(o)) errors.Add("ranges", $"{r} overlaps {other.Name} ({o}).");
        }
        errors.ThrowIfAny();
        Name = newName!;
        Ranges = list;
    }

    public void EnsureCanDelete()
    {
        if (IsDefault) throw new ConflictException("The Normal season can't be deleted.");
    }
}

public interface ISeasonRepository
{
    Task<IReadOnlyList<Season>> ListAsync(CancellationToken ct = default);
    Task<Season?> GetAsync(string id, CancellationToken ct = default);
    Task SaveAsync(Season season, CancellationToken ct = default);
    Task DeleteAsync(Season season, CancellationToken ct = default);
}

using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

[FirestoreData]
public sealed class SeasonRangeDocument
{
    [FirestoreProperty("start")]
    public string Start { get; set; } = string.Empty;

    [FirestoreProperty("end")]
    public string End { get; set; } = string.Empty;

    public static SeasonRangeDocument From(SeasonRange r) => new() { Start = r.Start.ToString(), End = r.End.ToString() };

    public SeasonRange ToDomain() => new(DayMonth.Parse(Start), DayMonth.Parse(End));
}

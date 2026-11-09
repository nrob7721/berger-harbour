using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

[FirestoreData]
public sealed class BoatRateDocument
{
    [FirestoreProperty("seasonId")]
    public string SeasonId { get; set; } = string.Empty;

    [FirestoreProperty("periodType")]
    public string PeriodType { get; set; } = string.Empty;

    [FirestoreProperty("priceCents")]
    public long PriceCents { get; set; }

    public static BoatRateDocument From(BoatRate rate) =>
        new() { SeasonId = rate.SeasonId.Value, PeriodType = rate.PeriodType.ToString(), PriceCents = FirestoreValues.Cents(rate.Price) };

    public BoatRate ToDomain() => new(new SeasonId(SeasonId), Enum.Parse<PeriodType>(PeriodType), FirestoreValues.Money(PriceCents));
}

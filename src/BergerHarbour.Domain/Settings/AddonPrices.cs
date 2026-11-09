namespace BergerHarbour.Domain.Settings;

/// <summary>An add-on's prices for the online period types. There are no add-on prices for LongWeekend or Custom.</summary>
public sealed record AddonPrices(decimal? Midweek, decimal? Weekend, decimal? Week)
{
    public static readonly AddonPrices None = new(null, null, null);
}

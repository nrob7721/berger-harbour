using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

public sealed record HirePriceQuote(Season Season, decimal Price);

/// <summary>Prices a stay per boat × season × period type. Prices are always calculated on the server.</summary>
public static class PricingService
{
    /// <summary>The season whose range contains the date, or the built-in Normal season.</summary>
    public static Season SeasonFor(DateOnly date, IReadOnlyCollection<Season> seasons) =>
        seasons.FirstOrDefault(s => !s.IsDefault && s.Contains(date))
        ?? seasons.FirstOrDefault(s => s.IsDefault)
        ?? throw new InvalidOperationException("The built-in Normal season is missing. Run the seed.");

    /// <summary>The boat's rate for the season containing the start date and the period type.</summary>
    public static HirePriceQuote QuoteHirePrice(Boat boat, IReadOnlyCollection<Season> seasons, PeriodType periodType,
        DateOnly startDate)
    {
        if (periodType == PeriodType.Custom)
        {
            throw new DomainValidationException("periodType", "Custom stays are priced by staff.");
        }

        var season = SeasonFor(startDate, seasons);
        var price = boat.RateFor(season.Id, periodType)
                    ?? throw new DomainValidationException("periodType",
                        $"{boat.Name} has no {season.Name} {periodType} rate.");
        return new HirePriceQuote(season, price);
    }

    /// <summary>The add-on's price for the period type, or null when it is price on request.</summary>
    public static decimal? AddonUnitPrice(AddonDefinition addon, PeriodType periodType) => addon.UnitPriceFor(periodType);
}

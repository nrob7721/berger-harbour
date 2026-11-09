using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>Prices per boat × season × period type (§7.4).</summary>
public sealed class PricingService
{
    /// <summary>The season whose range contains the start date, or the default Normal season.</summary>
    public Season SeasonFor(DateOnly startDate, IReadOnlyList<Season> seasons) =>
        seasons.FirstOrDefault(s => !s.IsDefault && s.Contains(startDate))
        ?? seasons.FirstOrDefault(s => s.IsDefault)
        ?? throw new InvalidOperationException("The default Normal season is missing.");

    /// <summary>The boat's rate for the season and period type, or null (Custom stays and missing rates: staff type it).</summary>
    public decimal? HirePrice(Boat boat, PeriodType periodType, DateOnly startDate, IReadOnlyList<Season> seasons) =>
        periodType == PeriodType.Custom ? null : boat.RateFor(SeasonFor(startDate, seasons).Id, periodType);

    /// <summary>The add-on's price for the period type, or null for price on request, LongWeekend and Custom.</summary>
    public decimal? AddonUnitPrice(AddonDefinition addon, PeriodType periodType) => addon.PriceFor(periodType);
}

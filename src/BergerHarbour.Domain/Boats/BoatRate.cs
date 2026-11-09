using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Boats;

/// <summary>A boat's hire price for one season and period type (Midweek, Weekend, Week or LongWeekend).</summary>
public sealed record BoatRate(SeasonId SeasonId, PeriodType PeriodType, decimal Price);

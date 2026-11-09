using System.Globalization;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>
/// The online rules about a stay's dates (§7.7 rules 2–5): period shape, minimum lead time, bookings-open-until and
/// blocked periods. Shared by the online booking strategy and the period availability query.
/// </summary>
public sealed class OnlineStayPolicy
{
    private readonly StayPeriodFactory periods = new();

    public OnlineStayCheck Check(
        PeriodType periodType, DateOnly startDate, BusinessSettings settings, IEnumerable<BlockedPeriod> blockedPeriods, DateOnly today)
    {
        var errors = new ValidationErrors();
        var stay = errors.Capture(() => (StayRange?)periods.ForOnlineStart(periodType, startDate));
        if (stay is not { } s)
        {
            return new OnlineStayCheck(null, errors);
        }

        if (s.Start < today.AddDays(settings.MinimumLeadTimeDays))
        {
            errors.Add("startDate", $"Online bookings must start at least {settings.MinimumLeadTimeDays} days from today. " +
                $"Please call or email us for earlier dates: {Contact(settings)}.");
        }

        if (s.LastNight > settings.BookingsOpenUntil)
        {
            errors.Add("startDate", $"Online bookings are open until {settings.BookingsOpenUntil.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}.");
        }

        var blocked = blockedPeriods.FirstOrDefault(p => p.Overlaps(s));
        if (blocked is not null)
        {
            errors.Add("startDate", $"Bookings over {blocked.Name} must be made by phone or email: {Contact(settings)}.");
        }

        return new OnlineStayCheck(s, errors);
    }

    private static string Contact(BusinessSettings settings) =>
        string.Join(" / ", new[] { settings.ContactPhone, settings.ContactEmail?.Value }.Where(v => !string.IsNullOrWhiteSpace(v)));
}

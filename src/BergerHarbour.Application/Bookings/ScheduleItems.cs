using BergerHarbour.Application.Bookings.Dtos;
using BergerHarbour.Domain.Bookings;

namespace BergerHarbour.Application.Bookings;

/// <summary>Milestones → the customer's schedule items, with each milestone's own amount.</summary>
public static class ScheduleItems
{
    public static IReadOnlyList<ScheduleItem> From(IReadOnlyList<PaymentMilestone> milestones)
    {
        var previous = 0m;
        var items = new List<ScheduleItem>();
        foreach (var m in milestones)
        {
            items.Add(new ScheduleItem(m.Kind, m.Label, m.RequiredCumulative, Math.Max(0m, m.RequiredCumulative - previous), m.DueDate, m.DueAtActivation));
            previous = Math.Max(previous, m.RequiredCumulative);
        }

        return items;
    }
}

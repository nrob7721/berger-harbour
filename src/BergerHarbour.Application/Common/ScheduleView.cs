using BergerHarbour.Application.Catalogue;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Common;

public static class ScheduleView
{
    public static IReadOnlyList<ScheduleItem> Build(IReadOnlyList<Milestone> milestones)
    {
        var result = new List<ScheduleItem>();
        var previous = 0m;
        foreach (var m in milestones)
        {
            var label = m.Kind switch
            {
                MilestoneKind.Deposit => "Deposit",
                MilestoneKind.Balance => "Balance",
                MilestoneKind.First => "First instalment (50%)",
                _ => "Final payment",
            };
            result.Add(new ScheduleItem(m.Kind, label, m.RequiredCumulative, Math.Max(0, m.RequiredCumulative - previous),
                SydneyTime.DateOf(m.DueAt), m.Kind == MilestoneKind.Deposit));
            previous = Math.Max(previous, m.RequiredCumulative);
        }
        return result;
    }
}

/// <summary>A third-party service (Stripe, Turnstile) failed. Maps to 502.</summary>
public sealed class ExternalServiceException(string message, Exception? inner = null) : Exception(message, inner);

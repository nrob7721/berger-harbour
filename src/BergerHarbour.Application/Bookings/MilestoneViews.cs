using BergerHarbour.Domain.Bookings;

namespace BergerHarbour.Application.Bookings;

public static class MilestoneViews
{
    /// <summary>
    /// Turns cumulative milestones into instalments for display. <paramref name="alreadyCovered"/> is what is (or
    /// will be at checkout) paid up front; later instalments only show what remains. A milestone whose due instant
    /// has passed is flagged <see cref="MilestoneDto.DueNow"/>.
    /// </summary>
    public static IReadOnlyList<MilestoneDto> Build(IReadOnlyList<PaymentMilestone> milestones, decimal alreadyCovered,
        bool depositIsDueNow, DateTimeOffset now)
    {
        var result = new List<MilestoneDto>();
        var covered = 0m;
        foreach (var m in milestones)
        {
            if (m.Kind == MilestoneKind.Deposit)
            {
                var upFront = Math.Max(m.RequiredCumulative, alreadyCovered);
                result.Add(new MilestoneDto(m.Kind, depositIsDueNow ? "Payable now" : "Deposit", upFront, m.RequiredCumulative,
                    depositIsDueNow ? null : m.DueDate, depositIsDueNow));
                covered = upFront;
                continue;
            }

            var instalment = Math.Max(0m, m.RequiredCumulative - covered);
            covered = Math.Max(covered, m.RequiredCumulative);
            if (instalment == 0 && depositIsDueNow)
            {
                continue;
            }

            result.Add(new MilestoneDto(m.Kind, Label(m.Kind), instalment, m.RequiredCumulative, m.DueDate, m.DueAt <= now));
        }

        return result;
    }

    public static string Label(MilestoneKind kind) => kind switch
    {
        MilestoneKind.Deposit => "Deposit",
        MilestoneKind.FirstInstalment => "First instalment (50%)",
        MilestoneKind.Final => "Final balance",
        _ => "Balance",
    };
}

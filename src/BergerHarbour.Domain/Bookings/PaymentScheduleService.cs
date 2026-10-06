using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>Offsets relative to check-in, from configuration so they can be shortened for testing.</summary>
public sealed record PaymentScheduleOffsets(
    TimeSpan BalanceDueBeforeHire,
    TimeSpan ExtendedFirstInstalmentBeforeHire,
    TimeSpan ExtendedFinalInstalmentBeforeHire,
    TimeSpan PaymentReminderBeforeDue)
{
    public static readonly PaymentScheduleOffsets Default = new(
        TimeSpan.FromDays(30), TimeSpan.FromDays(120), TimeSpan.FromDays(90), TimeSpan.FromDays(7));
}

public enum MilestoneKind
{
    Deposit,
    Balance,
    FirstInstalment,
    Final,
}

/// <summary>By <see cref="DueAt"/> the customer must have paid <see cref="RequiredCumulative"/> in total.</summary>
public sealed record PaymentMilestone(MilestoneKind Kind, decimal RequiredCumulative, DateTimeOffset DueAt)
{
    public DateOnly DueDate => SydneyTime.Today(DueAt);
}

public sealed class PaymentScheduleService(PaymentScheduleOffsets offsets)
{
    public PaymentScheduleOffsets Offsets { get; } = offsets;

    /// <summary>Extended when the stay overlaps a blocked period flagged for the extended schedule.</summary>
    public static PaymentSchedule DetermineSchedule(Stay stay, IEnumerable<BlockedPeriod> blockedPeriods) =>
        blockedPeriods.Any(p => p.UsesExtendedPaymentSchedule && stay.Overlaps(p.Nights))
            ? PaymentSchedule.Extended
            : PaymentSchedule.Standard;

    public IReadOnlyList<PaymentMilestone> Milestones(Booking booking) =>
        Milestones(booking.PaymentSchedule, booking.StartDate, booking.TotalPrice, booking.DepositAmount, booking.CreatedDate);

    /// <summary>
    /// The deposit (due at creation) followed by the Balance milestone (standard) or the First instalment (50%)
    /// and Final (100%) milestones (extended).
    /// </summary>
    public IReadOnlyList<PaymentMilestone> Milestones(PaymentSchedule schedule, DateOnly startDate, decimal totalPrice,
        decimal depositAmount, DateTimeOffset createdAt)
    {
        var checkIn = SydneyTime.CheckInInstant(startDate);
        var milestones = new List<PaymentMilestone>
        {
            new(MilestoneKind.Deposit, Math.Min(depositAmount, totalPrice), createdAt),
        };
        if (schedule == PaymentSchedule.Extended)
        {
            milestones.Add(new PaymentMilestone(MilestoneKind.FirstInstalment,
                Math.Round(totalPrice * 0.5m, 2, MidpointRounding.AwayFromZero),
                checkIn - Offsets.ExtendedFirstInstalmentBeforeHire));
            milestones.Add(new PaymentMilestone(MilestoneKind.Final, totalPrice, checkIn - Offsets.ExtendedFinalInstalmentBeforeHire));
        }
        else
        {
            milestones.Add(new PaymentMilestone(MilestoneKind.Balance, totalPrice, checkIn - Offsets.BalanceDueBeforeHire));
        }

        return milestones;
    }

    /// <summary>When the reminder for a milestone is sent; also when it starts counting toward "amount due now".</summary>
    public DateTimeOffset ReminderAt(PaymentMilestone milestone) => milestone.DueAt - Offsets.PaymentReminderBeforeDue;

    /// <summary>
    /// The largest shortfall among milestones whose reminder window has opened; when there is none, the whole
    /// amount owing (which covers ad-hoc links, e.g. after a price-on-request add-on is confirmed).
    /// </summary>
    public decimal AmountDueNow(Booking booking, DateTimeOffset now)
    {
        var paid = booking.AmountPaid;
        var owing = booking.AmountOwing;
        var largestShortfall = Milestones(booking)
            .Where(m => now >= ReminderAt(m))
            .Select(m => m.RequiredCumulative - paid)
            .DefaultIfEmpty(0m)
            .Max();
        return largestShortfall > 0 ? Math.Min(largestShortfall, owing) : owing;
    }

    /// <summary>
    /// What an online booking pays at checkout: min(deposit, hire price), or the full hire price when the final
    /// milestone falls due today or earlier (Australia/Sydney).
    /// </summary>
    public decimal OnlineCheckoutAmount(PaymentSchedule schedule, DateOnly startDate, decimal hirePrice,
        decimal depositAmount, DateTimeOffset now)
    {
        var final = Milestones(schedule, startDate, hirePrice, depositAmount, now)[^1];
        return final.DueDate <= SydneyTime.Today(now) ? hirePrice : Math.Min(depositAmount, hirePrice);
    }
}

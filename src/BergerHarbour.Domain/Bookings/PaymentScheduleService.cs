using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>Standard and extended payment schedules (§7.6).</summary>
public sealed class PaymentScheduleService
{
    private readonly PaymentScheduleOptions options;

    public PaymentScheduleService(PaymentScheduleOptions options)
    {
        this.options = options;
    }

    public PaymentScheduleOptions Options => options;

    /// <summary>The online checkout always charges only min(deposit, hire price), even when the balance is already due.</summary>
    public static decimal CheckoutAmount(decimal depositAmount, decimal hirePrice) => Math.Min(depositAmount, hirePrice);

    /// <summary>Check-in: StartDate 11:00 Australia/Sydney.</summary>
    public static DateTimeOffset CheckInInstant(DateOnly startDate) => Sydney.InstantOf(startDate, Sydney.StandardCheckIn);

    /// <summary>Extended when the stay overlaps a blocked period flagged for the extended schedule (Christmas/New Year).</summary>
    public PaymentScheduleType ScheduleFor(StayRange stay, IEnumerable<BlockedPeriod> blockedPeriods) =>
        blockedPeriods.Any(p => p.UsesExtendedPaymentSchedule && p.Overlaps(stay)) ? PaymentScheduleType.Extended : PaymentScheduleType.Standard;

    public IReadOnlyList<PaymentMilestone> Milestones(Booking booking) =>
        Milestones(booking.PaymentSchedule, booking.StartDate, booking.DepositAmount, booking.TotalPrice, booking.CreatedDate);

    public IReadOnlyList<PaymentMilestone> Milestones(
        PaymentScheduleType schedule, DateOnly startDate, decimal depositAmount, decimal totalPrice, DateTimeOffset createdAt)
    {
        var checkIn = CheckInInstant(startDate);
        var milestones = new List<PaymentMilestone>
        {
            Milestone(MilestoneKind.Deposit, "Deposit", Math.Min(depositAmount, totalPrice), createdAt, createdAt),
        };
        if (schedule == PaymentScheduleType.Extended)
        {
            milestones.Add(Milestone(MilestoneKind.First, "First instalment (50%)", Math.Round(totalPrice / 2m, 2, MidpointRounding.AwayFromZero),
                checkIn - options.ExtendedFirstInstalmentBeforeHire, createdAt));
            milestones.Add(Milestone(MilestoneKind.Final, "Final payment", totalPrice, checkIn - options.ExtendedFinalInstalmentBeforeHire, createdAt));
        }
        else
        {
            milestones.Add(Milestone(MilestoneKind.Balance, "Balance", totalPrice, checkIn - options.BalanceDueBeforeHire, createdAt));
        }

        return milestones;
    }

    /// <summary>When the payment reminder for a milestone is sent.</summary>
    public DateTimeOffset ReminderAt(PaymentMilestone milestone) => milestone.DueAt - options.PaymentReminderBeforeDue;

    /// <summary>When the pre-hire instructions are sent.</summary>
    public DateTimeOffset PreHireInstructionsAt(DateOnly startDate) => CheckInInstant(startDate) - options.PreHireInstructionsBeforeHire;

    /// <summary>
    /// The largest shortfall among milestones whose reminder window has opened; when there is none, the amount owing.
    /// </summary>
    public decimal AmountDueNow(Booking booking, DateTimeOffset now) =>
        AmountDueNow(Milestones(booking), booking.AmountPaid, booking.AmountOwing, now);

    public decimal AmountDueNow(IReadOnlyList<PaymentMilestone> milestones, decimal amountPaid, decimal amountOwing, DateTimeOffset now)
    {
        var shortfalls = milestones
            .Where(m => now >= ReminderAt(m) || m.Kind == MilestoneKind.Deposit)
            .Select(m => m.ShortfallFor(amountPaid))
            .Where(s => s > 0m)
            .ToList();
        return shortfalls.Count == 0 ? amountOwing : Math.Min(shortfalls.Max(), amountOwing);
    }

    private static PaymentMilestone Milestone(MilestoneKind kind, string label, decimal required, DateTimeOffset due, DateTimeOffset createdAt) =>
        new(kind, label, required, due, Sydney.DateOf(due), due <= createdAt);
}

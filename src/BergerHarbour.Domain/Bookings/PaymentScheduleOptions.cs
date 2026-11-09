namespace BergerHarbour.Domain.Bookings;

/// <summary>Payment milestone offsets relative to check-in (StartDate 11:00 Sydney), from configuration.</summary>
public sealed record PaymentScheduleOptions(
    TimeSpan BalanceDueBeforeHire,
    TimeSpan ExtendedFirstInstalmentBeforeHire,
    TimeSpan ExtendedFinalInstalmentBeforeHire,
    TimeSpan PaymentReminderBeforeDue,
    TimeSpan PreHireInstructionsBeforeHire)
{
    public static readonly PaymentScheduleOptions Default = new(
        TimeSpan.FromDays(30), TimeSpan.FromDays(120), TimeSpan.FromDays(90), TimeSpan.FromDays(7), TimeSpan.FromDays(7));
}

using BergerHarbour.Domain.Bookings;

namespace BergerHarbour.Application.Notifications;

/// <summary><c>Notifications__*</c> payment schedule and email timing offsets (§10.2).</summary>
public sealed class NotificationOptions
{
    public const string Section = "Notifications";

    public TimeSpan BalanceDueBeforeHire { get; set; } = TimeSpan.FromDays(30);

    public TimeSpan ExtendedFirstInstalmentBeforeHire { get; set; } = TimeSpan.FromDays(120);

    public TimeSpan ExtendedFinalInstalmentBeforeHire { get; set; } = TimeSpan.FromDays(90);

    public TimeSpan PaymentReminderBeforeDue { get; set; } = TimeSpan.FromDays(7);

    public TimeSpan PreHireInstructionsBeforeHire { get; set; } = TimeSpan.FromDays(7);

    public PaymentScheduleOptions ToScheduleOptions() => new(
        BalanceDueBeforeHire, ExtendedFirstInstalmentBeforeHire, ExtendedFinalInstalmentBeforeHire, PaymentReminderBeforeDue,
        PreHireInstructionsBeforeHire);
}

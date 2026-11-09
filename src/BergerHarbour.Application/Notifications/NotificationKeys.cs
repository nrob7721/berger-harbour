using BergerHarbour.Domain.Bookings;

namespace BergerHarbour.Application.Notifications;

/// <summary>Idempotency keys recorded in <see cref="Booking.SentNotifications"/>.</summary>
public static class NotificationKeys
{
    public const string BookingConfirmed = "BookingConfirmed";
    public const string StaffNewOnlineBooking = "StaffNewOnlineBooking";
    public const string PreHire = "PreHire";

    public static string PaymentDueReminder(MilestoneKind kind) => $"PaymentDue:{kind}:Reminder";

    public static string PaymentDue(MilestoneKind kind) => $"PaymentDue:{kind}:Due";

    public static string Overdue(MilestoneKind kind) => $"Overdue:{kind}";

    public static string Superseded(BookingId bookingId) => $"Superseded:{bookingId.Value}";
}

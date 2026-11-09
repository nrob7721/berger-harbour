namespace BergerHarbour.Domain.Settings;

/// <summary>The emails the system sends (§9). Numbers 3 and 9 are deliberately not implemented.</summary>
public enum EmailTemplateKey
{
    BookingConfirmed,
    StaffNewOnlineBooking,
    PaymentDue,
    PaymentReceived,
    PreHireInstructions,
    StaffBalanceOverdue,
    StaffStandbySuperseded,
    StaffAbandonedBooking,
}

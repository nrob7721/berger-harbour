namespace BergerHarbour.Domain.Bookings;

/// <summary>The booking lifecycle (§7.3).</summary>
public enum BookingStatus
{
    PendingPayment,
    Active,
    Superseded,
    Cancelled,
    Expired,
}

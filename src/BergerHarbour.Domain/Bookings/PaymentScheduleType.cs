namespace BergerHarbour.Domain.Bookings;

/// <summary>Standard (100% 30 days before) or Extended (Christmas/New Year: 50% at 120 days, 100% at 90 days).</summary>
public enum PaymentScheduleType
{
    Standard,
    Extended,
}

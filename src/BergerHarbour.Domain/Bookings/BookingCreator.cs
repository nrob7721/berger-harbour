namespace BergerHarbour.Domain.Bookings;

/// <summary>Who created a booking or recorded a payment: the customer online, or staff.</summary>
public enum BookingCreator
{
    Customer,
    SystemUser,
}

namespace BergerHarbour.Domain.Bookings;

/// <summary>An email already sent for a booking, keyed for idempotency (e.g. <c>PaymentDue:Balance:Reminder</c>).</summary>
public sealed record SentNotification(string Key, DateTimeOffset SentAt);

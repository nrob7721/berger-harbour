using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;

namespace BergerHarbour.Application.Abstractions;

/// <summary>All time access goes through this, so tests control time.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public enum CheckoutPurpose { Deposit, Payment }

public sealed record CheckoutRequest(
    string BookingId,
    CheckoutPurpose Purpose,
    decimal Amount,
    string Description,
    string? CustomerEmail,
    string? Reference,
    string ReturnUrl,
    DateTimeOffset? ExpiresAt);

public sealed record CheckoutSession(string SessionId, string ClientSecret);

public enum ExpireSessionResult { Expired, AlreadyExpired, AlreadyCompleted }

/// <summary>Stripe Embedded Checkout behind a port.</summary>
public interface IPaymentGateway
{
    Task<CheckoutSession> CreateEmbeddedCheckoutAsync(CheckoutRequest request, CancellationToken ct = default);
    Task<ExpireSessionResult> ExpireCheckoutSessionAsync(string sessionId, CancellationToken ct = default);
}

public sealed record EmailMessage(string To, string Subject, string HtmlBody);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

public interface ITurnstileVerifier
{
    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default);
}

/// <summary>The suffix of a booking reference: how many references already used the prefix. Incremented transactionally.</summary>
public interface IBookingReferenceGenerator
{
    Task<int> NextSuffixAsync(string prefix, CancellationToken ct = default);
}

/// <summary>
/// A per-boat exclusive section for every write that creates or moves occupancy for a boat.
/// Reads made through the session are consistent with the writes, which are committed together when the work returns.
/// </summary>
public interface IBoatScheduleLock
{
    Task<T> RunExclusiveAsync<T>(BoatId boatId, Func<IBoatScheduleSession, Task<T>> work, CancellationToken ct = default);
}

public interface IBoatScheduleSession
{
    BoatId BoatId { get; }
    /// <summary>The boat's Active and PendingPayment bookings (stand-bys included) that may overlap the stay.</summary>
    Task<IReadOnlyList<Booking>> GetPotentiallyOverlappingBookingsAsync(StayRange stay);
    /// <summary>The boat's unavailabilities with a night inside the stay.</summary>
    Task<IReadOnlyList<BoatUnavailability>> GetUnavailabilitiesAsync(StayRange stay);
    Task<Booking?> GetBookingAsync(string bookingId);
    Task<BoatUnavailability?> GetUnavailabilityAsync(string unavailabilityId);
    void SaveBooking(Booking booking);
    void SaveUnavailability(BoatUnavailability unavailability);
}

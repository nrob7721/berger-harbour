using BergerHarbour.Domain.Bookings;

namespace BergerHarbour.Application.Bookings;

/// <summary>
/// The outcome of <see cref="BookingActivator.TryActivateAsync"/>: the availability check, and the stand-bys the
/// activation superseded (empty when the dates were taken and nothing changed).
/// </summary>
public sealed record ActivationAttempt(AvailabilityCheck Check, IReadOnlyList<Booking> Superseded)
{
    public bool Activated => Check.IsAvailable;
}

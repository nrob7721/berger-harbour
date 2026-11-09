using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Application.Bookings.Validation;

/// <summary>An add-on quantity in a booking request.</summary>
public sealed record BookingRequestAddon(AddonId AddonId, int Quantity);

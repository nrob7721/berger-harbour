using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Domain.BookingDrafts;

/// <summary>An add-on a customer asked for, with its quantity.</summary>
public sealed record AddonRequest(AddonId AddonId, int Quantity);

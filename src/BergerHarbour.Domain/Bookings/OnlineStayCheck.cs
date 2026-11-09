using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>The result of the online stay rules: the stay when its shape is valid, and every rule broken.</summary>
public sealed record OnlineStayCheck(StayRange? Stay, ValidationErrors Errors)
{
    public bool IsValid => Stay is not null && Errors.IsEmpty;
}

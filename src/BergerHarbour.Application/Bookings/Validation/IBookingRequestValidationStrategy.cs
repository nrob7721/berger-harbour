using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings.Validation;

/// <summary>A channel's booking rules. Synchronous and pure: no constructor dependencies, no I/O, no clock.</summary>
public interface IBookingRequestValidationStrategy
{
    ValidationErrors Validate(BookingRequest request, BookingValidationContext context);
}

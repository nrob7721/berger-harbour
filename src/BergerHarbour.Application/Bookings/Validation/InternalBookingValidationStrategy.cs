using BergerHarbour.Application.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings.Validation;

/// <summary>
/// The staff booking rules: start before end, adults ≥ 1, children ≥ 0 and a valid customer. Everything else is a
/// non-blocking warning (<see cref="StaffBookingWarnings"/>). Registered in the admin API.
/// </summary>
public sealed class InternalBookingValidationStrategy : IBookingRequestValidationStrategy
{
    public ValidationErrors Validate(BookingRequest request, BookingValidationContext context)
    {
        var errors = new ValidationErrors();
        if (request.EndDate is not { } end || end <= request.StartDate)
        {
            errors.Add("endDate", "The end date must be after the start date.");
        }

        if (request.NumberOfAdults < 1)
        {
            errors.Add("numberOfAdults", "At least 1 adult is required.");
        }

        if (request.NumberOfChildren < 0)
        {
            errors.Add("numberOfChildren", "Children can't be negative.");
        }

        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            errors.Add("firstName", "First name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.LastName))
        {
            errors.Add("lastName", "Last name is required.");
        }

        errors.Capture(() => EmailAddress.Create(request.Email));
        errors.Capture(() => MobileNumber.CreateOptional(request.Mobile, "mobile"));
        errors.Capture(() => MobileNumber.CreateOptional(request.SecondaryMobile, "secondaryMobile"));
        errors.Capture(() => request.Address.ToAddress());
        return errors;
    }
}

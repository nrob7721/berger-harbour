using BergerHarbour.Application.Common;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings.Validation;

/// <summary>
/// Staff bookings: only StartDate &lt; EndDate, at least one adult, no negative children and a valid customer.
/// Staff may book any dates, including blocked periods and stays shorter than the lead time. Guest, bed, add-on
/// capacity and changeover-time limits are non-blocking warnings (<see cref="StaffBookingWarnings"/>).
/// </summary>
public sealed class InternalBookingValidationStrategy : IBookingRequestValidationStrategy
{
    public ValidationErrors Validate(BookingRequest request, BookingValidationContext context)
    {
        var errors = new ValidationErrors();
        if (request.EndDate is not { } end)
        {
            errors.Add("endDate", "End date is required.");
        }
        else if (request.StartDate >= end)
        {
            errors.Add("endDate", "The end date must be after the start date.");
        }

        if (request.NumberOfAdults < 1)
        {
            errors.Add("numberOfAdults", "There must be at least one adult.");
        }

        if (request.NumberOfChildren < 0)
        {
            errors.Add("numberOfChildren", "Number of children cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            errors.Add("fullName", "Full name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors.Add("email", "Email is required.");
        }
        else if (!EmailAddress.IsValid(request.Email))
        {
            errors.Add("email", "Enter a valid email address.");
        }

        if (!string.IsNullOrWhiteSpace(request.Mobile) && !PhoneNumber.IsValid(request.Mobile))
        {
            errors.Add("phone", "Enter a valid phone number, including the country code for overseas numbers.");
        }

        return errors;
    }
}

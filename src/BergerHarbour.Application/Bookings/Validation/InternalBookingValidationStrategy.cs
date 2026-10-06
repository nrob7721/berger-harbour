using BergerHarbour.Application.Common;
using BergerHarbour.Domain.Customers;

namespace BergerHarbour.Application.Bookings.Validation;

/// <summary>
/// Staff bookings: only StartDate &lt; EndDate, NumberOfGuests &gt;= 1 and a valid customer. Staff may book any
/// dates, including blocked periods and stays shorter than the lead time.
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

        if (request.NumberOfGuests < 1)
        {
            errors.Add("numberOfGuests", "Number of guests must be at least 1.");
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

        return errors;
    }
}

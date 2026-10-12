using BergerHarbour.Application.Common;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings.Validation;

/// <summary>The customer's name, email and mobile, as entered online. The mobile must be valid for its country.</summary>
public static class ContactValidation
{
    public static ValidationErrors Validate(ValidationErrors errors, string? fullName, string? email, string? mobile)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            errors.Add("fullName", "Full name is required.");
        }
        else if (fullName.Trim().Length > 200)
        {
            errors.Add("fullName", "Full name must be 200 characters or fewer.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            errors.Add("email", "Email is required.");
        }
        else if (!EmailAddress.IsValid(email))
        {
            errors.Add("email", "Enter a valid email address.");
        }

        if (string.IsNullOrWhiteSpace(mobile))
        {
            errors.Add("mobile", "Mobile number is required.");
        }
        else if (!PhoneNumber.IsValid(mobile))
        {
            errors.Add("mobile", "Enter a valid mobile number for the selected country.");
        }

        return errors;
    }
}

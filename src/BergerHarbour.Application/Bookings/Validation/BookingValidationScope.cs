namespace BergerHarbour.Application.Bookings.Validation;

/// <summary><c>Quote</c> skips the draft, terms, rooming acceptance and contact details; <c>Submit</c> checks everything.</summary>
public enum BookingValidationScope
{
    Quote,
    Submit,
}

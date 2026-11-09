namespace BergerHarbour.Application.Payments.Dtos;

/// <summary>The pay page: what the booking costs and what is due now.</summary>
public sealed record PaymentLinkDetails(
    string Reference,
    string CustomerFirstName,
    string BoatName,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalPrice,
    decimal AmountPaid,
    decimal AmountOwing,
    decimal AmountDueNow);

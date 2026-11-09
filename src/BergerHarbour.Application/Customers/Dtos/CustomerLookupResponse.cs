using BergerHarbour.Application.Bookings.Dtos;

namespace BergerHarbour.Application.Customers.Dtos;

/// <summary><c>GET /api/customers/by-email</c>: the customer and their Frequent Floater status anchored at now.</summary>
public sealed record CustomerLookupResponse(
    AdminCustomer Customer,
    FrequentFloaterDto FrequentFloater);

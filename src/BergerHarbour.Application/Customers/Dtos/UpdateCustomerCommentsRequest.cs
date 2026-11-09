namespace BergerHarbour.Application.Customers.Dtos;

/// <summary><c>PUT /api/customers/{id}/comments</c>.</summary>
public sealed record UpdateCustomerCommentsRequest(
    string? Comments,
    int Version);

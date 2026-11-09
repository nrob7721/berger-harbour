namespace BergerHarbour.Application.Customers.Dtos;

/// <summary>A customer as staff see them.</summary>
public sealed record AdminCustomer(
    string Id,
    string FirstName,
    string LastName,
    string Email,
    string? Mobile,
    string? SecondaryMobile,
    string? Address,
    string? Comments,
    DateTimeOffset CreatedDate,
    int Version);

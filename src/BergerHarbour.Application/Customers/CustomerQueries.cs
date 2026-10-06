using BergerHarbour.Domain.Customers;

namespace BergerHarbour.Application.Customers;

public sealed record CustomerDto(string Id, string FullName, string Email, string? MobileNumber, DateTimeOffset CreatedDate);

/// <summary>The Customers admin page is read-only in v1.</summary>
public sealed class CustomerQueries(ICustomerRepository customers)
{
    public async Task<IReadOnlyList<CustomerDto>> ListAsync(string? q, CancellationToken ct = default)
    {
        var all = await customers.ListAsync(ct);
        return Filter(all, q).OrderBy(c => c.FullName, StringComparer.OrdinalIgnoreCase).Select(ToDto).ToList();
    }

    public async Task<CustomerDto?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        if (!EmailAddress.IsValid(email))
        {
            return null;
        }

        var customer = await customers.GetByEmailAsync(EmailAddress.Create(email), ct);
        return customer is null ? null : ToDto(customer);
    }

    /// <summary>Case-insensitive match on name, email or mobile.</summary>
    public static IEnumerable<Customer> Filter(IEnumerable<Customer> all, string? q)
    {
        var term = q?.Trim();
        if (string.IsNullOrEmpty(term))
        {
            return all;
        }

        return all.Where(c => c.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                              c.Email.Value.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                              (c.MobileNumber?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    public static CustomerDto ToDto(Customer c) => new(c.Id.Value, c.FullName, c.Email.Value, c.MobileNumber, c.CreatedDate);
}

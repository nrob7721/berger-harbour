using BergerHarbour.Application.Abstractions;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Customers;

/// <summary>Customers are matched by normalised email; booking again updates the name and mobile.</summary>
public sealed class CustomerService(ICustomerRepository customers, IClock clock)
{
    private const int MaxAttempts = 8;

    public async Task<Customer> UpsertAsync(string fullName, string email, string? mobile, CancellationToken ct = default)
    {
        var address = EmailAddress.Create(email);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var existing = await customers.GetByEmailAsync(address, ct);
                if (existing is null)
                {
                    var created = Customer.Create(fullName, address.Value, mobile, clock.UtcNow);
                    await customers.SaveAsync(created, ct);
                    return created;
                }

                if (existing.UpdateContactDetails(fullName, mobile, clock.UtcNow))
                {
                    await customers.SaveAsync(existing, ct);
                }

                return existing;
            }
            catch (Exception ex) when (attempt < MaxAttempts &&
                                       ex is DuplicateCustomerEmailException or ConcurrencyConflictException)
            {
                // Someone else created or changed this customer at the same moment; re-read and retry.
            }
        }
    }
}

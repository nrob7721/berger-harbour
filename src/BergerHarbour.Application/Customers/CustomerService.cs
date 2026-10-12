using BergerHarbour.Application.Abstractions;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Customers;

/// <summary>Customers are matched by normalised email; booking again updates the name and mobile.</summary>
public sealed class CustomerService(ICustomerRepository customers, IClock clock)
{
    private const int MaxAttempts = 8;

    /// <summary>Online: called when the booking becomes Active, from the details the customer entered.</summary>
    public Task<Customer> UpsertAsync(string fullName, string email, string? mobile, CancellationToken ct = default) =>
        UpsertAsync(fullName, email, mobile, null, false, ct);

    /// <summary>Staff: the booking dialog also sets (or clears) the secondary phone.</summary>
    public Task<Customer> UpsertFromStaffAsync(string fullName, string email, string? phone, string? secondaryPhone,
        CancellationToken ct = default) =>
        UpsertAsync(fullName, email, phone, secondaryPhone, true, ct);

    private async Task<Customer> UpsertAsync(string fullName, string email, string? mobile, string? secondaryPhone,
        bool setSecondaryPhone, CancellationToken ct)
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
                    if (setSecondaryPhone)
                    {
                        created.ChangeSecondaryPhone(secondaryPhone, clock.UtcNow);
                    }

                    await customers.SaveAsync(created, ct);
                    return created;
                }

                var changed = existing.UpdateContactDetails(fullName, mobile, clock.UtcNow);
                if (setSecondaryPhone)
                {
                    changed |= existing.ChangeSecondaryPhone(secondaryPhone, clock.UtcNow);
                }

                if (changed)
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

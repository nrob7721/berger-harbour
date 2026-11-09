using BergerHarbour.Application.Abstractions;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Customers;

/// <summary>Creates or updates customers, matched by normalised email (unique).</summary>
public sealed class CustomerRegistry
{
    private const int Attempts = 3;
    private readonly ICustomerRepository customers;
    private readonly IClock clock;

    public CustomerRegistry(ICustomerRepository customers, IClock clock)
    {
        this.customers = customers;
        this.clock = clock;
    }

    /// <summary>
    /// Online activation: creates the customer, or updates an existing one's name and mobile to the latest values. The
    /// secondary mobile, address and comments are never changed here.
    /// </summary>
    public async Task<Customer> UpsertFromOnlineAsync(CustomerDetails details, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var existing = await customers.GetByEmailAsync(details.Email, ct);
                if (existing is not null)
                {
                    existing.UpdateFromBooking(details.FirstName, details.LastName, details.MobileNumber, clock.UtcNow);
                    await customers.SaveAsync(existing, ct);
                    return existing;
                }

                var customer = Customer.Create(CustomerId.New(), details.FirstName, details.LastName, details.Email, details.MobileNumber, clock.UtcNow);
                await customers.SaveAsync(customer, ct);
                return customer;
            }
            catch (Exception e) when (attempt < Attempts && e is DuplicateCustomerEmailException or ConcurrencyException)
            {
                // Another request created or changed this customer meanwhile: reload and apply again.
            }
        }
    }

    /// <summary>
    /// A staff booking's customer: matched or created by email, then every contact detail is saved. If the email changed
    /// to one no customer has, the booking's current customer takes the new email.
    /// </summary>
    public async Task<Customer> SaveFromStaffAsync(CustomerId? currentCustomerId, StaffContact contact, CancellationToken ct = default)
    {
        var customer = await customers.GetByEmailAsync(contact.Email, ct);
        if (customer is null && currentCustomerId is { } currentId)
        {
            customer = await customers.GetAsync(currentId, ct);
        }

        customer ??= Customer.Create(CustomerId.New(), contact.FirstName, contact.LastName, contact.Email, contact.Mobile, clock.UtcNow);
        customer.UpdateByStaff(contact.FirstName, contact.LastName, contact.Email, contact.Mobile, contact.SecondaryMobile, contact.Address,
            contact.Comments, clock.UtcNow);
        try
        {
            await customers.SaveAsync(customer, ct);
        }
        catch (DuplicateCustomerEmailException)
        {
            throw new DomainValidationException("email", "Another customer was just saved with this email. Please try again.");
        }

        return customer;
    }
}

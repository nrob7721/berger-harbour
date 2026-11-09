using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Customers;

/// <summary>Matches customers by normalised email, creating them when needed.</summary>
public sealed class CustomerRegistry(ICustomerRepository customers, IClock clock)
{
    /// <summary>Online activation: create, or update names and mobile. Secondary mobile, address and comments are never touched.</summary>
    public async Task<Customer> UpsertFromOnlineBookingAsync(CustomerDetails d, CancellationToken ct = default)
    {
        async Task<Customer> Attempt()
        {
            var existing = await customers.GetByEmailAsync(EmailAddress.Normalise(d.Email), ct);
            var customer = existing ?? Customer.Create(d.FirstName, d.LastName, d.Email, d.MobileNumber, clock.UtcNow);
            if (existing is not null) existing.UpdateFromOnlineBooking(d.FirstName, d.LastName, d.MobileNumber);
            await customers.SaveAsync(customer, ct);
            return customer;
        }
        try { return await Attempt(); }
        catch (Exception e) when (e is ConflictException or ConcurrencyConflictException)
        {
            return await Attempt(); // another request created or changed the same email meanwhile
        }
    }

    /// <summary>
    /// Staff booking dialog: the matching customer (or a new one, not yet saved) with the dialog's contact details, address and comments applied.
    /// Saved by <see cref="CommitStaffChangesAsync"/> once the booking itself has been saved.
    /// </summary>
    public async Task<Customer> PrepareFromStaffAsync(ContactInput c, string? customerComments, CancellationToken ct = default)
    {
        var email = EmailAddress.Require(c.Email);
        var customer = await customers.GetByEmailAsync(email, ct)
                       ?? Customer.Create(c.FirstName ?? "", c.LastName ?? "", email, c.Mobile, clock.UtcNow);
        customer.UpdateContactDetails(c.FirstName ?? "", c.LastName ?? "", c.Mobile, c.SecondaryMobile, c.Address);
        customer.UpdateComments(customerComments);
        return customer;
    }

    /// <summary>Saves a prepared customer. When another request created the same email meanwhile, applies the changes to that customer instead.</summary>
    public async Task<Customer> CommitStaffChangesAsync(Customer prepared, CancellationToken ct = default)
    {
        try
        {
            await customers.SaveAsync(prepared, ct);
            return prepared;
        }
        catch (Exception e) when (e is ConflictException or ConcurrencyConflictException)
        {
            var current = await customers.GetByEmailAsync(prepared.Email, ct);
            if (current is null) throw;
            current.UpdateContactDetails(prepared.FirstName, prepared.LastName, prepared.MobileNumber, prepared.SecondaryMobileNumber, prepared.Address);
            current.UpdateComments(prepared.Comments);
            await customers.SaveAsync(current, ct);
            return current;
        }
    }
}

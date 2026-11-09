using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Customers;

/// <summary>The Customers page. Only customer comments are edited here.</summary>
public sealed class CustomerAdminService(ICustomerRepository customers, IBookingRepository bookings, IBoatRepository boats, IClock clock)
{
    public async Task<IReadOnlyList<AdminCustomer>> ListAsync(string? q, CancellationToken ct = default)
    {
        var term = (q ?? "").Trim();
        bool Matches(Customer c) => term.Length == 0
            || c.FullName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || c.Email.Contains(term, StringComparison.OrdinalIgnoreCase)
            || (c.MobileNumber ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);
        return (await customers.ListAsync(ct)).Where(Matches)
            .OrderBy(c => c.LastName, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.FirstName, StringComparer.OrdinalIgnoreCase)
            .Select(AdminBookingService.ToAdminCustomer).ToList();
    }

    /// <summary>Pre-fills the booking dialog. The Frequent Floater check is anchored at now (a booking being created).</summary>
    public async Task<CustomerLookupResponse> GetByEmailAsync(string? email, CancellationToken ct = default)
    {
        var customer = await customers.GetByEmailAsync(EmailAddress.Normalise(email), ct) ?? throw new NotFoundException("No customer with that email.");
        var ff = FrequentFloaterService.Evaluate(clock.UtcNow, null, await bookings.ListByCustomerAsync(customer.Id, ct));
        return new CustomerLookupResponse(AdminBookingService.ToAdminCustomer(customer), new FrequentFloaterDto(ff.IsFrequentFloater, ff.OtherBookingCount));
    }

    public async Task<AdminCustomer> UpdateCommentsAsync(string id, UpdateCustomerCommentsRequest r, CancellationToken ct = default)
    {
        var customer = await customers.GetAsync(id, ct) ?? throw new NotFoundException("Customer not found.");
        customer.EnsureVersion(r.Version);
        customer.UpdateComments(r.Comments);
        await customers.SaveAsync(customer, ct);
        return AdminBookingService.ToAdminCustomer(customer);
    }

    /// <summary>All statuses, newest stay first.</summary>
    public async Task<IReadOnlyList<CustomerBookingItem>> ListBookingsAsync(string id, CancellationToken ct = default)
    {
        _ = await customers.GetAsync(id, ct) ?? throw new NotFoundException("Customer not found.");
        var names = (await boats.ListAsync(ct)).ToDictionary(b => b.Id, b => b.Name);
        return (await bookings.ListByCustomerAsync(id, ct)).OrderByDescending(b => b.StartDate)
            .Select(b => new CustomerBookingItem(b.Id, b.Reference, names.GetValueOrDefault(b.BoatId, ""), b.StartDate, b.EndDate,
                b.PeriodType, b.Status, b.PaymentStatus, b.IsStandby))
            .ToList();
    }
}

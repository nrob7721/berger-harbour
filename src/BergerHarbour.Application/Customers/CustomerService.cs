using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings.Dtos;
using BergerHarbour.Application.Customers.Dtos;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Customers;

/// <summary>The Customers page and the booking dialog's email lookup (<c>CustomersController</c>). Only comments are edited here.</summary>
public sealed class CustomerService
{
    private readonly ICustomerRepository customers;
    private readonly IBookingRepository bookings;
    private readonly IBoatRepository boats;
    private readonly FrequentFloaterService frequentFloaters;
    private readonly IClock clock;

    public CustomerService(
        ICustomerRepository customers, IBookingRepository bookings, IBoatRepository boats, FrequentFloaterService frequentFloaters, IClock clock)
    {
        this.customers = customers;
        this.bookings = bookings;
        this.boats = boats;
        this.frequentFloaters = frequentFloaters;
        this.clock = clock;
    }

    public async Task<IReadOnlyList<AdminCustomer>> ListAsync(string? q, CancellationToken ct = default)
    {
        var term = (q ?? string.Empty).Trim();
        return (await customers.ListAsync(ct))
            .Where(c => term.Length == 0 || c.FullName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || c.Email.Value.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.LastName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.FirstName, StringComparer.OrdinalIgnoreCase)
            .Select(c => c.ToDto())
            .ToList();
    }

    /// <summary>Pre-fills the booking dialog; the Frequent Floater check is anchored at now. 404 when no customer has the email.</summary>
    public async Task<CustomerLookupResponse> GetByEmailAsync(string? email, CancellationToken ct = default)
    {
        var customer = await customers.GetByEmailAsync(EmailAddress.Create(email), ct)
            ?? throw new NotFoundException("No customer has that email.");
        var result = frequentFloaters.Evaluate(clock.UtcNow, await bookings.ListByCustomerAsync(customer.Id, ct), null);
        return new CustomerLookupResponse(customer.ToDto(), new FrequentFloaterDto(result.IsFrequentFloater, result.OtherBookingCount));
    }

    public async Task<AdminCustomer> UpdateCommentsAsync(string id, UpdateCustomerCommentsRequest request, CancellationToken ct = default)
    {
        var customer = await customers.GetAsync(new CustomerId(id), ct) ?? throw new NotFoundException("We couldn't find that customer.");
        customer.EnsureVersion(request.Version);
        customer.UpdateComments(request.Comments, clock.UtcNow);
        await customers.SaveAsync(customer, ct);
        return customer.ToDto();
    }

    /// <summary>All of the customer's bookings, every status, newest start date first.</summary>
    public async Task<IReadOnlyList<CustomerBookingItem>> ListBookingsAsync(string id, CancellationToken ct = default)
    {
        var customer = await customers.GetAsync(new CustomerId(id), ct) ?? throw new NotFoundException("We couldn't find that customer.");
        var names = (await boats.ListAsync(ct)).ToDictionary(b => b.Id, b => b.Name);
        return (await bookings.ListByCustomerAsync(customer.Id, ct))
            .OrderByDescending(b => b.StartDate)
            .Select(b => new CustomerBookingItem(b.Id.Value, b.Reference?.Value, names.GetValueOrDefault(b.BoatId, string.Empty), b.StartDate, b.EndDate,
                b.PeriodType, b.Status, b.PaymentStatus, b.IsStandby))
            .ToList();
    }
}

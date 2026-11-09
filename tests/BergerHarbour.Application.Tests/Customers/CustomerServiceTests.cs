using BergerHarbour.Application.Customers;
using BergerHarbour.Application.Customers.Dtos;
using BergerHarbour.Application.Tests.Support;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Tests.Customers;

public class CustomerServiceTests
{
    private readonly ApplicationHarness h = new();

    [Fact]
    public async Task Comments_are_saved_with_a_version_check()
    {
        await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19"));
        var customer = (await h.Get<CustomerService>().ListAsync(null)).Single();
        var saved = await h.Get<CustomerService>().UpdateCommentsAsync(customer.Id, new UpdateCustomerCommentsRequest("Regular", customer.Version));
        Assert.Equal("Regular", saved.Comments);
        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            h.Get<CustomerService>().UpdateCommentsAsync(customer.Id, new UpdateCustomerCommentsRequest("Stale", customer.Version)));
    }

    [Fact]
    public async Task A_customers_bookings_are_newest_first_with_every_status()
    {
        var first = await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19"));
        await h.Staff.UpdateAsync(first.Booking.Id, ApplicationHarness.UpdateRequest(first.Booking, r => r with
        {
            Status = Domain.Bookings.BookingStatus.Cancelled,
        }));
        await h.Staff.CreateAsync(h.StaffRequest("2027-05-03", "2027-05-07"));
        var bookings = await h.Get<CustomerService>().ListBookingsAsync(first.Booking.Customer!.Id);
        Assert.Equal(["2027-05-03", "2027-01-15"], bookings.Select(b => b.StartDate.ToString("yyyy-MM-dd")));
    }

    [Fact]
    public async Task Lookup_rejects_an_invalid_email() =>
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Get<CustomerService>().GetByEmailAsync("jane@gmail"));
}

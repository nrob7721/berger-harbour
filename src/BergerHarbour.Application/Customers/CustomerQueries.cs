using BergerHarbour.Application.Common;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Customers;

public sealed record CustomerDto(
    string Id,
    string FullName,
    string Email,
    string? MobileNumber,
    string? SecondaryPhone,
    DateTimeOffset CreatedDate);

/// <summary>One row of a customer's booking history on the Customers page.</summary>
public sealed record CustomerBookingDto(
    string Id,
    string Reference,
    string BoatId,
    string BoatName,
    DateOnly StartDate,
    DateOnly EndDate,
    PeriodType PeriodType,
    BookingStatus Status,
    bool IsStandby,
    PaymentStatus PaymentStatus);

/// <summary>
/// The Customers admin page is read-only in v1, apart from opening a customer's bookings.
/// </summary>
public sealed class CustomerQueries(ICustomerRepository customers, IBookingRepository bookings, IBoatRepository boats)
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

    /// <summary>All of the customer's bookings, any status, newest stay first.</summary>
    public async Task<IReadOnlyList<CustomerBookingDto>> ListBookingsAsync(string customerId, CancellationToken ct = default)
    {
        var customer = await customers.GetAsync(new CustomerId(customerId ?? string.Empty), ct)
                       ?? throw new NotFoundException("Customer not found.");
        var list = await bookings.ListByCustomerAsync([customer.Id], ct);
        var boatNames = (await boats.ListAsync(ct)).ToDictionary(b => b.Id, b => b.Name);
        return list.OrderByDescending(b => b.StartDate).ThenByDescending(b => b.CreatedDate).Select(b =>
            new CustomerBookingDto(b.Id.Value, b.Reference.Value, b.BoatId.Value, boatNames.GetValueOrDefault(b.BoatId, "?"),
                b.StartDate, b.EndDate, b.PeriodType, b.Status, b.IsStandby, b.PaymentStatus)).ToList();
    }

    /// <summary>Case-insensitive match on name, email, mobile or secondary phone.</summary>
    public static IEnumerable<Customer> Filter(IEnumerable<Customer> all, string? q)
    {
        var term = q?.Trim();
        if (string.IsNullOrEmpty(term))
        {
            return all;
        }

        var digits = new string(term.Where(char.IsDigit).ToArray());
        return all.Where(c => c.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                              c.Email.Value.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                              MatchesPhone(c.MobileNumber, term, digits) || MatchesPhone(c.SecondaryPhone, term, digits));
    }

    public static CustomerDto ToDto(Customer c) =>
        new(c.Id.Value, c.FullName, c.Email.Value, c.MobileNumber, c.SecondaryPhone, c.CreatedDate);

    private static bool MatchesPhone(string? phone, string term, string digits) =>
        phone is not null && (phone.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                              (digits.Length >= 4 && new string(Format.Phone(phone).Where(char.IsDigit).ToArray()).Contains(digits)));
}

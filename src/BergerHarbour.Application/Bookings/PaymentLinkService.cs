using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Catalogue;
using BergerHarbour.Application.Common;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Application.Bookings;

/// <summary>The pay page reached from payment links in emails.</summary>
public sealed class PaymentLinkService(
    IBookingRepository bookings,
    IBoatRepository boats,
    ICustomerRepository customers,
    IPaymentGateway payments,
    PaymentScheduleService schedule,
    IOptions<AppUrlOptions> urls,
    IClock clock)
{
    private const string InvalidLink = "This payment link is no longer valid. Please contact us.";

    private async Task<Booking> FindAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) throw new NotFoundException(InvalidLink);
        var booking = await bookings.GetByPaymentLinkHashAsync(PaymentLinkTokens.Hash(token), ct);
        if (booking is null || booking.Status != BookingStatus.Active) throw new NotFoundException(InvalidLink);
        return booking;
    }

    public async Task<PaymentLinkDetails> GetAsync(string token, CancellationToken ct = default)
    {
        var b = await FindAsync(token, ct);
        var boat = await boats.GetAsync(b.BoatId, ct);
        var customer = b.CustomerId is { } id ? await customers.GetAsync(id, ct) : null;
        return new PaymentLinkDetails(b.Reference ?? "", customer?.FirstName ?? b.CustomerDetails?.FirstName ?? "", boat?.Name ?? "",
            b.StartDate, b.EndDate, b.TotalPrice, b.AmountPaid, b.AmountOwing, schedule.AmountDueNow(b, clock.UtcNow));
    }

    public async Task<CheckoutResponse> CreateCheckoutAsync(string token, CancellationToken ct = default)
    {
        var b = await FindAsync(token, ct);
        var amount = schedule.AmountDueNow(b, clock.UtcNow);
        if (amount <= 0) throw new DomainValidationException("amount", "Nothing is owing on this booking.");
        var boat = await boats.GetAsync(b.BoatId, ct);
        var customer = b.CustomerId is { } id ? await customers.GetAsync(id, ct) : null;
        try
        {
            var session = await payments.CreateEmbeddedCheckoutAsync(new CheckoutRequest(
                b.Id, CheckoutPurpose.Payment, amount, $"Payment – booking {b.Reference} ({boat?.Name})",
                customer?.Email ?? b.CustomerDetails?.Email, b.Reference,
                $"{urls.Value.BaseUrl}/pay/?token={Uri.EscapeDataString(token)}&paid=1", null), ct);
            return new CheckoutResponse(session.ClientSecret, session.SessionId);
        }
        catch (Exception e) when (e is not DomainValidationException)
        {
            throw new ExternalServiceException("We couldn't start the payment. Please try again in a moment.", e);
        }
    }
}

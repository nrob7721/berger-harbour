using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Common;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Application.Payments;

public sealed record PayPageDto(
    string Reference,
    string BoatName,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalPrice,
    decimal AmountPaid,
    decimal AmountOwing,
    decimal AmountDueNow,
    bool CanPay);

public sealed record PayCheckoutResult(string ClientSecret, string SessionId, decimal Amount);

/// <summary>The pay page reached from payment links in emails.</summary>
public sealed class PaymentLinkService(
    IBookingRepository bookings,
    IBoatRepository boats,
    ICustomerRepository customers,
    IPaymentGateway paymentGateway,
    PaymentScheduleService paymentSchedule,
    IOptions<BookingOptions> bookingOptions,
    IClock clock)
{
    public async Task<PayPageDto> GetAsync(string token, CancellationToken ct = default)
    {
        var booking = await FindAsync(token, ct);
        var boat = await boats.GetAsync(booking.BoatId, ct);
        var due = AmountDueNow(booking);
        return new PayPageDto(booking.Reference.Value, boat?.Name ?? string.Empty, booking.StartDate, booking.EndDate,
            booking.TotalPrice, booking.AmountPaid, booking.AmountOwing, due, due > 0);
    }

    public async Task<PayCheckoutResult> CreateCheckoutAsync(string token, CancellationToken ct = default)
    {
        var booking = await FindAsync(token, ct);
        var amount = AmountDueNow(booking);
        if (amount <= 0)
        {
            throw RequestValidationException.For("token", "There is nothing to pay on this booking.");
        }

        var boat = await boats.GetAsync(booking.BoatId, ct);
        var customer = await customers.GetAsync(booking.CustomerId, ct)
                       ?? throw new NotFoundException("Booking not found.");
        var session = await paymentGateway.CreateEmbeddedCheckoutSessionAsync(new CheckoutSessionRequest(
            booking.Id, booking.Reference.Value, CheckoutPurpose.Payment, amount,
            $"Payment for {boat?.Name} {Format.Date(booking.StartDate)} ({booking.Reference.Value})",
            customer.Email.Value,
            clock.UtcNow + bookingOptions.Value.CheckoutSessionLifetime + TimeSpan.FromMinutes(1)), ct);
        return new PayCheckoutResult(session.ClientSecret, session.SessionId, amount);
    }

    /// <summary>Only Active bookings can be paid through a link.</summary>
    private decimal AmountDueNow(Booking booking) =>
        booking.Status == BookingStatus.Active ? paymentSchedule.AmountDueNow(booking, clock.UtcNow) : 0m;

    private async Task<Booking> FindAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
        {
            throw new NotFoundException("This payment link is not valid.");
        }

        return await bookings.GetByPaymentLinkTokenHashAsync(PaymentLinkTokens.Hash(token.Trim()), ct)
               ?? throw new NotFoundException("This payment link is not valid.");
    }
}

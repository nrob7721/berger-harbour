using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Payments.Dtos;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Payments;

/// <summary>The pay page reached from emailed payment links (<c>PaymentsController</c>).</summary>
public sealed class PaymentLinkService
{
    public const string InvalidLink = "This payment link isn't valid. Please contact us.";
    public const string NothingOwing = "Nothing is owing on this booking.";

    private readonly IBookingRepository bookings;
    private readonly IBoatRepository boats;
    private readonly ICustomerRepository customers;
    private readonly PaymentScheduleService schedule;
    private readonly IPaymentGateway payments;
    private readonly PaymentLinkTokens links;
    private readonly IClock clock;

    public PaymentLinkService(
        IBookingRepository bookings, IBoatRepository boats, ICustomerRepository customers, PaymentScheduleService schedule, IPaymentGateway payments,
        PaymentLinkTokens links, IClock clock)
    {
        this.bookings = bookings;
        this.boats = boats;
        this.customers = customers;
        this.schedule = schedule;
        this.payments = payments;
        this.links = links;
        this.clock = clock;
    }

    public async Task<PaymentLinkDetails> GetAsync(string token, CancellationToken ct = default)
    {
        var booking = await FindAsync(token, ct);
        var boat = await boats.GetAsync(booking.BoatId, ct);
        var customer = booking.CustomerId is { } id ? await customers.GetAsync(id, ct) : null;
        var dueNow = booking.IsNotifiable ? schedule.AmountDueNow(booking, clock.UtcNow) : 0m;
        return new PaymentLinkDetails(booking.Reference!.Value, customer?.FirstName ?? booking.CustomerDetails?.FirstName ?? string.Empty,
            boat?.Name ?? string.Empty, booking.StartDate, booking.EndDate, booking.TotalPrice, booking.AmountPaid, booking.AmountOwing, dueNow);
    }

    /// <summary>An Embedded Checkout session for the amount due now. 400 if nothing is owing.</summary>
    public async Task<CheckoutResponse> CheckoutAsync(string token, CancellationToken ct = default)
    {
        var booking = await FindAsync(token, ct);
        if (!booking.IsNotifiable)
        {
            throw new BusinessRuleException("This booking can't be paid online. Please contact us.");
        }

        var amount = schedule.AmountDueNow(booking, clock.UtcNow);
        if (amount <= 0m)
        {
            throw new BusinessRuleException(NothingOwing);
        }

        var boat = await boats.GetAsync(booking.BoatId, ct);
        var customer = booking.CustomerId is { } id ? await customers.GetAsync(id, ct) : null;
        var email = customer?.Email.Value ?? booking.CustomerDetails?.Email.Value ?? string.Empty;
        var session = await payments.CreateCheckoutSessionAsync(new CheckoutSessionRequest(booking.Id, CheckoutPurpose.Payment, amount,
            $"Booking {booking.Reference!.Value}", $"{boat?.Name} {Notifications.DisplayFormat.Date(booking.StartDate)}", email,
            $"{links.BaseUrl}/pay/?token={Uri.EscapeDataString(token)}&session_id={{CHECKOUT_SESSION_ID}}", null), ct);
        return new CheckoutResponse(session.ClientSecret, session.SessionId);
    }

    private async Task<Booking> FindAsync(string token, CancellationToken ct)
    {
        var booking = string.IsNullOrWhiteSpace(token) ? null : await bookings.GetByPaymentLinkTokenHashAsync(PaymentLinkTokens.Hash(token), ct);
        return booking is { Reference: not null } ? booking : throw new NotFoundException(InvalidLink);
    }
}

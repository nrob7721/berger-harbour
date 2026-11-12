using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Customers;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings;

/// <summary>
/// Activation of an online booking (§7.3), shared by the Stripe deposit webhook and staff confirmation: the reference,
/// the availability check and stand-by superseding inside the boat lock, then the Customer and the booking draft. Each
/// caller sends its own emails.
/// </summary>
public sealed class BookingActivator
{
    private readonly IBookingRepository bookings;
    private readonly IBookingDraftRepository drafts;
    private readonly IBookingReferenceGenerator references;
    private readonly BookingAvailabilityService availability;
    private readonly CustomerRegistry customers;
    private readonly IClock clock;

    public BookingActivator(
        IBookingRepository bookings, IBookingDraftRepository drafts, IBookingReferenceGenerator references, BookingAvailabilityService availability,
        CustomerRegistry customers, IClock clock)
    {
        this.bookings = bookings;
        this.drafts = drafts;
        this.references = references;
        this.availability = availability;
        this.customers = customers;
        this.clock = clock;
    }

    /// <summary>
    /// The booking's reference, or a new one from the online customer's last name and the start date. A new one uses up
    /// a suffix even if the activation then fails.
    /// </summary>
    public async Task<BookingReference> ReferenceForAsync(Booking booking, CancellationToken ct = default)
    {
        if (booking.Reference is { } existing)
        {
            return existing;
        }

        var details = booking.CustomerDetails ?? throw new InvalidOperationException("An online booking has no customer details.");
        var prefix = BookingReference.PrefixFor(details.LastName, booking.StartDate);
        return BookingReference.Create(prefix, await references.NextSuffixAsync(prefix, ct));
    }

    /// <summary>
    /// Inside the boat lock: activates <paramref name="current"/> (which already has its reference) when its dates are
    /// still free, and supersedes the stand-bys it overlaps. When the dates are taken, nothing changes.
    /// </summary>
    public async Task<ActivationAttempt> TryActivateAsync(IBoatScheduleSession session, Booking current, DateTimeOffset now)
    {
        var overlapping = await session.ListOverlappingBookingsAsync(current.Stay);
        var check = availability.Check(current.Stay, overlapping, await session.ListOverlappingUnavailabilitiesAsync(current.Stay), now, current.Id);
        if (!check.IsAvailable)
        {
            return new ActivationAttempt(check, []);
        }

        current.ActivateFromDeposit(now);
        var superseded = availability.StandbysSupersededBy(current, overlapping);
        foreach (var standby in superseded)
        {
            standby.Supersede(now);
            session.SaveBooking(standby);
        }

        return new ActivationAttempt(check, superseded);
    }

    /// <summary>
    /// After the lock: creates or updates the Customer from the booking's <see cref="CustomerDetails"/> snapshot, links it,
    /// and marks the booking draft converted (unless it already is). Safe to repeat.
    /// </summary>
    public async Task CompleteAsync(Booking activated, CancellationToken ct = default)
    {
        if (activated.CustomerId is null && activated.CreatedBy == BookingCreator.Customer)
        {
            var customer = await customers.UpsertFromOnlineAsync(activated.CustomerDetails!, ct);
            activated.AttachCustomer(customer.Id, clock.UtcNow);
            await bookings.SaveAsync(activated, ct);
        }

        if (activated.BookingDraftId is { } draftId && await drafts.GetAsync(draftId, ct) is { IsConverted: false } draft)
        {
            draft.MarkConverted(activated.Id, clock.UtcNow);
            await drafts.SaveAsync(draft, ct);
        }
    }
}

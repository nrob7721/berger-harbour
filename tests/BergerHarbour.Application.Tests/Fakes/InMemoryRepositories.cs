using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;

namespace BergerHarbour.Application.Tests.Fakes;

public sealed class InMemoryBookingRepository(InMemoryStore store) : IBookingRepository
{
    private IEnumerable<Booking> All() => store.Bookings.All().Select(Booking.FromSnapshot);

    public Task<Booking?> GetAsync(BookingId id, CancellationToken ct = default) =>
        Task.FromResult(store.Bookings.Get(id.Value) is { } s ? Booking.FromSnapshot(s) : null);

    public Task<Booking?> GetByReferenceAsync(BookingReference reference, CancellationToken ct = default) =>
        Task.FromResult(All().FirstOrDefault(b => b.Reference == reference));

    public Task<Booking?> GetByPaymentLinkTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        Task.FromResult(All().FirstOrDefault(b => b.PaymentLinkTokenHash == tokenHash));

    public Task<IReadOnlyList<Booking>> ListOverlappingAsync(DateOnly from, DateOnly to,
        IReadOnlyCollection<BookingStatus>? statuses = null, BoatId? boatId = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Booking>>(All().Where(b => b.StartDate < to && b.EndDate > from &&
                                                                 (statuses is null || statuses.Contains(b.Status)) &&
                                                                 (boatId is null || b.BoatId == boatId)).ToList());

    public Task<IReadOnlyList<Booking>> ListByCustomerAsync(IReadOnlyCollection<CustomerId> customerIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Booking>>(All().Where(b => customerIds.Contains(b.CustomerId)).ToList());

    public Task<IReadOnlyList<Booking>> ListByStatusAsync(BookingStatus status, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Booking>>(All().Where(b => b.Status == status).ToList());

    public Task<IReadOnlyList<Booking>> ListByStatusStartingFromAsync(BookingStatus status, DateOnly startDateFrom,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Booking>>(All().Where(b => b.Status == status && b.StartDate >= startDateFrom).ToList());

    public Task SaveAsync(Booking booking, CancellationToken ct = default)
    {
        Save(booking);
        return Task.CompletedTask;
    }

    internal void Save(Booking booking)
    {
        store.Bookings.Save(booking.Id.Value, booking.Version, v => booking.ToSnapshot() with { Version = v });
        booking.MarkPersisted();
    }
}

public sealed class InMemoryBoatRepository(InMemoryStore store) : IBoatRepository
{
    public Task<Boat?> GetAsync(BoatId id, CancellationToken ct = default) =>
        Task.FromResult(store.Boats.Get(id.Value) is { } s ? Boat.FromSnapshot(s) : null);

    public Task<Boat?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        Task.FromResult(store.Boats.All().Where(b => b.Slug == slug).Select(Boat.FromSnapshot).FirstOrDefault());

    public Task<IReadOnlyList<Boat>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Boat>>(store.Boats.All().Select(Boat.FromSnapshot).ToList());

    public Task SaveAsync(Boat boat, CancellationToken ct = default)
    {
        store.Boats.Save(boat.Id.Value, boat.Version, v => boat.ToSnapshot() with { Version = v });
        boat.MarkPersisted();
        return Task.CompletedTask;
    }
}

public sealed class InMemoryCustomerRepository(InMemoryStore store) : ICustomerRepository
{
    public Task<Customer?> GetAsync(CustomerId id, CancellationToken ct = default) =>
        Task.FromResult(store.Customers.Get(id.Value) is { } s ? Customer.FromSnapshot(s) : null);

    public Task<Customer?> GetByEmailAsync(EmailAddress email, CancellationToken ct = default) =>
        Task.FromResult(store.Customers.All().Where(c => c.Email == email.Value).Select(Customer.FromSnapshot).FirstOrDefault());

    public Task<IReadOnlyList<Customer>> GetManyAsync(IEnumerable<CustomerId> ids, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Customer>>(ids.Select(i => store.Customers.Get(i.Value)).OfType<CustomerSnapshot>()
            .Select(Customer.FromSnapshot).ToList());

    public Task<IReadOnlyList<Customer>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Customer>>(store.Customers.All().Select(Customer.FromSnapshot).ToList());

    public Task SaveAsync(Customer customer, CancellationToken ct = default)
    {
        if (customer.IsNew && store.Customers.All().Any(c => c.Email == customer.Email.Value))
        {
            throw new DuplicateCustomerEmailException(customer.Email.Value);
        }

        store.Customers.Save(customer.Id.Value, customer.Version, v => customer.ToSnapshot() with { Version = v });
        customer.MarkPersisted();
        return Task.CompletedTask;
    }
}

public sealed class InMemoryUnavailabilityRepository(InMemoryStore store) : IBoatUnavailabilityRepository
{
    public Task<BoatUnavailability?> GetAsync(UnavailabilityId id, CancellationToken ct = default) =>
        Task.FromResult(store.Unavailabilities.Get(id.Value) is { } s ? BoatUnavailability.FromSnapshot(s) : null);

    public Task<IReadOnlyList<BoatUnavailability>> ListByBoatAsync(BoatId boatId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BoatUnavailability>>(store.Unavailabilities.All().Where(u => u.BoatId == boatId.Value)
            .Select(BoatUnavailability.FromSnapshot).ToList());

    public Task<IReadOnlyList<BoatUnavailability>> ListOverlappingAsync(DateOnly from, DateOnly to, BoatId? boatId = null,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BoatUnavailability>>(store.Unavailabilities.All()
            .Where(u => u.FirstNight < to && u.LastNight >= from && (boatId is null || u.BoatId == boatId.Value.Value))
            .Select(BoatUnavailability.FromSnapshot).ToList());

    public Task DeleteAsync(BoatUnavailability unavailability, CancellationToken ct = default)
    {
        store.Unavailabilities.Remove(unavailability.Id.Value);
        return Task.CompletedTask;
    }

    internal void Save(BoatUnavailability u)
    {
        store.Unavailabilities.Save(u.Id.Value, u.Version, v => u.ToSnapshot() with { Version = v });
        u.MarkPersisted();
    }
}

public sealed class InMemorySeasonRepository(InMemoryStore store) : ISeasonRepository
{
    public Task<IReadOnlyList<Season>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Season>>(store.Seasons.All().Select(Season.FromSnapshot).ToList());

    public Task<Season?> GetAsync(SeasonId id, CancellationToken ct = default) =>
        Task.FromResult(store.Seasons.Get(id.Value) is { } s ? Season.FromSnapshot(s) : null);

    public Task SaveAsync(Season season, CancellationToken ct = default)
    {
        store.Seasons.Save(season.Id.Value, season.Version, v => season.ToSnapshot() with { Version = v });
        season.MarkPersisted();
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Season season, CancellationToken ct = default)
    {
        store.Seasons.Remove(season.Id.Value);
        return Task.CompletedTask;
    }
}

public sealed class InMemoryBlockedPeriodRepository(InMemoryStore store) : IBlockedPeriodRepository
{
    public Task<IReadOnlyList<BlockedPeriod>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BlockedPeriod>>(store.BlockedPeriods.All().Select(BlockedPeriod.FromSnapshot).ToList());

    public Task<BlockedPeriod?> GetAsync(BlockedPeriodId id, CancellationToken ct = default) =>
        Task.FromResult(store.BlockedPeriods.Get(id.Value) is { } s ? BlockedPeriod.FromSnapshot(s) : null);

    public Task SaveAsync(BlockedPeriod blockedPeriod, CancellationToken ct = default)
    {
        store.BlockedPeriods.Save(blockedPeriod.Id.Value, blockedPeriod.Version, v => blockedPeriod.ToSnapshot() with { Version = v });
        blockedPeriod.MarkPersisted();
        return Task.CompletedTask;
    }

    public Task DeleteAsync(BlockedPeriod blockedPeriod, CancellationToken ct = default)
    {
        store.BlockedPeriods.Remove(blockedPeriod.Id.Value);
        return Task.CompletedTask;
    }
}

public sealed class InMemoryAddonRepository(InMemoryStore store) : IAddonDefinitionRepository
{
    public Task<IReadOnlyList<AddonDefinition>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AddonDefinition>>(store.Addons.All().Select(AddonDefinition.FromSnapshot).ToList());

    public Task<AddonDefinition?> GetAsync(AddonId id, CancellationToken ct = default) =>
        Task.FromResult(store.Addons.Get(id.Value) is { } s ? AddonDefinition.FromSnapshot(s) : null);

    public Task SaveAsync(AddonDefinition addon, CancellationToken ct = default)
    {
        store.Addons.Save(addon.Id.Value, addon.Version, v => addon.ToSnapshot() with { Version = v });
        addon.MarkPersisted();
        return Task.CompletedTask;
    }
}

public sealed class InMemoryEmailTemplateRepository(InMemoryStore store) : IEmailTemplateRepository
{
    public Task<IReadOnlyList<EmailTemplate>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EmailTemplate>>(store.Templates.All().Select(EmailTemplate.FromSnapshot).ToList());

    public Task<EmailTemplate?> GetAsync(EmailTemplateKey key, CancellationToken ct = default) =>
        Task.FromResult(store.Templates.Get(key) is { } s ? EmailTemplate.FromSnapshot(s) : null);

    public Task SaveAsync(EmailTemplate template, CancellationToken ct = default)
    {
        store.Templates.Save(template.Key, template.Version, v => template.ToSnapshot() with { Version = v });
        template.MarkPersisted();
        return Task.CompletedTask;
    }
}

public sealed class InMemoryBusinessSettingsRepository(InMemoryStore store) : IBusinessSettingsRepository
{
    public async Task<BusinessSettings> GetAsync(CancellationToken ct = default) =>
        await FindAsync(ct) ?? throw new InvalidOperationException("Not seeded");

    public Task<BusinessSettings?> FindAsync(CancellationToken ct = default) =>
        Task.FromResult(store.Business.Get(BusinessSettings.SingletonId) is { } s ? BusinessSettings.FromSnapshot(s) : null);

    public Task SaveAsync(BusinessSettings settings, CancellationToken ct = default)
    {
        store.Business.Save(BusinessSettings.SingletonId, settings.Version, v => settings.ToSnapshot() with { Version = v });
        settings.MarkPersisted();
        return Task.CompletedTask;
    }
}

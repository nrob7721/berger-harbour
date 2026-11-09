using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Unavailabilities;

namespace BergerHarbour.Application.Tests.Fakes;

/// <summary>The tables behind the in-memory repositories.</summary>
public sealed class InMemoryDatabase
{
    public InMemoryAggregates<BoatId, Boat> Boats { get; } = new(b => b.Id, AggregateCopies.Copy);

    public InMemoryAggregates<BookingId, Booking> Bookings { get; } = new(b => b.Id, AggregateCopies.Copy);

    public InMemoryAggregates<BookingDraftId, BookingDraft> Drafts { get; } = new(d => d.Id, AggregateCopies.Copy);

    public InMemoryAggregates<CustomerId, Customer> Customers { get; } = new(c => c.Id, AggregateCopies.Copy);

    public InMemoryAggregates<BoatUnavailabilityId, BoatUnavailability> Unavailabilities { get; } = new(u => u.Id, AggregateCopies.Copy);

    public InMemoryAggregates<SeasonId, Season> Seasons { get; } = new(s => s.Id, AggregateCopies.Copy);

    public InMemoryAggregates<BlockedPeriodId, BlockedPeriod> BlockedPeriods { get; } = new(p => p.Id, AggregateCopies.Copy);

    public InMemoryAggregates<AddonId, AddonDefinition> Addons { get; } = new(a => a.Id, AggregateCopies.Copy);

    public InMemoryAggregates<EmailTemplateKey, EmailTemplate> Templates { get; } = new(t => t.Key, AggregateCopies.Copy);

    public InMemoryAggregates<string, BusinessSettings> Settings { get; } = new(_ => "business", AggregateCopies.Copy);

    /// <summary>Serialises the fake boat schedule lock.</summary>
    public SemaphoreSlim ScheduleGate { get; } = new(1, 1);
}

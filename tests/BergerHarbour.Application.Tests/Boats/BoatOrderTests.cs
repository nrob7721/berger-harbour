using BergerHarbour.Application.Boats;
using BergerHarbour.Application.Tests.Support;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Tests.Boats;

/// <summary>Boat order in both <c>GET /api/boats</c> responses (decision 39).</summary>
public class BoatOrderTests
{
    private readonly ApplicationHarness h = new();

    private decimal? NormalWeekRate(Boat boat) => boat.RateFor(h.Db.Seasons.All.Single(s => s.IsDefault).Id, PeriodType.Week);

    private static void AssertBoatOrder(IReadOnlyList<Boat> boats, Func<Boat, decimal?> rate)
    {
        for (var i = 1; i < boats.Count; i++)
        {
            var (a, b) = (rate(boats[i - 1]), rate(boats[i]));
            Assert.True(
                b is null ? true : a is not null && (a > b || (a == b && string.Compare(boats[i - 1].Name, boats[i].Name, StringComparison.OrdinalIgnoreCase) < 0)),
                $"{boats[i - 1].Name} ({a}) is listed before {boats[i].Name} ({b}).");
        }
    }

    [Fact]
    public async Task Admin_boats_are_in_boat_order()
    {
        var listed = (await h.Get<BoatService>().ListAsync()).Select(b => h.Boat(b.Slug)).ToList();
        Assert.Equal(h.Db.Boats.All.Count, listed.Count);
        Assert.NotEqual(listed.Select(b => b.Name), listed.OrderByDescending(b => b.Berth).ThenBy(b => b.Name).Select(b => b.Name));
        AssertBoatOrder(listed, NormalWeekRate);
    }

    [Fact]
    public async Task Public_boats_are_in_boat_order()
    {
        foreach (var boat in h.Db.Boats.All.Where(b => b.Type == BoatType.House))
        {
            h.Activate(boat.Slug);
        }

        var listed = (await h.Get<BoatCatalogueService>().ListAsync()).Boats.Select(b => h.Boat(b.Slug)).ToList();
        Assert.True(listed.Count > 1);
        AssertBoatOrder(listed, NormalWeekRate);
    }
}

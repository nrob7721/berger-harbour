using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BergerHarbour.Application.Bookings.Dtos;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Infrastructure.IntegrationTests.Support;
using BergerHarbour.Infrastructure.Web;

namespace BergerHarbour.Infrastructure.IntegrationTests.Firestore;

[Collection(EmulatorCollection.Name)]
public class FirestoreBoatScheduleLockTests(EmulatorFixture fx)
{
    [Fact]
    public async Task Two_concurrent_bookings_for_the_same_boat_and_dates_have_exactly_one_winner()
    {
        var requests = new[]
        {
            await fx.StaffRequestAsync("2029-01-08", "2029-01-12", email: "race-a@example.com", lastName: "Alpha"),
            await fx.StaffRequestAsync("2029-01-10", "2029-01-15", email: "race-b@example.com", lastName: "Bravo"),
        };
        var results = await Task.WhenAll(requests.Select(async r =>
        {
            try
            {
                await fx.Staff().CreateAsync(r);
                return "ok";
            }
            catch (ConflictException e)
            {
                return e.Message;
            }
        }));
        Assert.Equal(1, results.Count(r => r == "ok"));
        Assert.Equal(1, results.Count(r => r == ConflictException.DatesTaken));
        var stored = await fx.Get<IBookingRepository>().ListOverlappingAsync(EmulatorFixture.D("2029-01-08"), EmulatorFixture.D("2029-01-15"));
        var boatId = (await fx.BoatAsync("pacific-blue")).Id;
        Assert.Single(stored, b => b.BoatId == boatId && b.Status == BookingStatus.Active);
    }

    [Fact]
    public async Task Through_the_admin_api_the_loser_gets_409_those_dates_are_no_longer_available()
    {
        EmulatorFixture.RequireEmulator();
        await using var factory = new ApiFactory<AdminApi.Controllers.JobsController>(ConfigKeys.LocalEnvironment, new Dictionary<string, string?>
        {
            [ConfigKeys.FirestoreProjectId] = "test-" + Guid.NewGuid().ToString("N")[..12],
        });
        var client = factory.CreateClient();
        var boats = await client.GetFromJsonAsync<JsonElement>("/api/boats");
        var boatId = boats.EnumerateArray().First(b => b.GetProperty("slug").GetString() == "pacific-blue").GetProperty("id").GetString()!;
        CreateStaffBookingRequest Request(string email, string start, string end) => new(boatId, PeriodType.Custom, EmulatorFixture.D(start),
            EmulatorFixture.D(end), 2, 0, null, null, false, null, null, 4000, email, "John", "Smith", null, null, null, []);
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/bookings", Request("http-a@example.com", "2029-02-05", "2029-02-09"), Json),
            client.PostAsJsonAsync("/api/bookings", Request("http-b@example.com", "2029-02-07", "2029-02-12"), Json));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var loser = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var problem = await loser.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ConflictException.DatesTaken, problem.GetProperty("title").GetString());
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}

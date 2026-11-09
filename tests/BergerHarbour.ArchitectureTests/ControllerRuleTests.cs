using System.Reflection;
using BergerHarbour.ArchitectureTests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BergerHarbour.ArchitectureTests;

/// <summary>Thin controllers: one constructor taking exactly one application service (and optionally a logger), one service per controller.</summary>
public class ControllerRuleTests
{
    public static TheoryData<string> Apis() => new(Assemblies.Apis.Select(a => a.GetName().Name!));

    [Theory]
    [MemberData(nameof(Apis))]
    public void Every_controller_takes_exactly_one_application_service_and_optionally_a_logger(string api)
    {
        foreach (var controller in Controllers(api))
        {
            var constructor = Assert.Single(controller.GetConstructors());
            var parameters = constructor.GetParameters().Select(p => p.ParameterType).ToList();
            var services = parameters.Where(IsApplicationService).ToList();
            var loggers = parameters.Where(p => p.IsGenericType && p.GetGenericTypeDefinition() == typeof(ILogger<>)).ToList();
            Assert.True(services.Count == 1, $"{controller.Name} must take exactly one application service.");
            Assert.True(parameters.Count == services.Count + loggers.Count && loggers.Count <= 1, $"{controller.Name} takes something other than its service.");
        }
    }

    [Theory]
    [MemberData(nameof(Apis))]
    public void No_two_controllers_of_an_api_use_the_same_service(string api)
    {
        var services = Controllers(api).Select(c => c.GetConstructors().Single().GetParameters().Single(p => IsApplicationService(p.ParameterType)).ParameterType).ToList();
        Assert.Equal(services.Count, services.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Apis))]
    public void Controllers_live_in_the_controllers_folder(string api) =>
        Assert.All(Controllers(api), c => Assert.Equal($"{api}.Controllers", c.Namespace));

    [Fact]
    public void The_apis_have_exactly_the_specified_controllers()
    {
        AssertSame(
            ["AddonAvailabilityController", "AvailabilityController", "BoatQuotesController", "BoatsController", "BookingDraftsController", "BookingsController",
                "PaymentsController", "StripeWebhookController"],
            Controllers("BergerHarbour.PublicApi").Select(c => c.Name));
        AssertSame(
            ["AddonAvailabilityController", "AddonDefinitionsController", "BlockedPeriodsController", "BoatUnavailabilitiesController", "BoatsController",
                "BookingsController", "BusinessSettingsController", "CustomersController", "EmailTemplatesController", "JobsController", "SeasonsController"],
            Controllers("BergerHarbour.AdminApi").Select(c => c.Name));
    }

    private static void AssertSame(IEnumerable<string> expected, IEnumerable<string> actual) =>
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));

    private static bool IsApplicationService(Type type) => type.Assembly == Assemblies.Application && type.Name.EndsWith("Service", StringComparison.Ordinal);

    private static IEnumerable<Type> Controllers(string api) =>
        Assemblies.Types(Assemblies.Apis.Single(a => a.GetName().Name == api)).Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(ControllerBase)));
}

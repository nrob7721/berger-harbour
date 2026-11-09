using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.ArchitectureTests.Support;

namespace BergerHarbour.ArchitectureTests;

/// <summary>Ports are implemented outside Application; validation strategies are pure.</summary>
public class ApplicationRuleTests
{
    private const string Abstractions = "BergerHarbour.Application.Abstractions";

    [Fact]
    public void No_application_type_implements_a_port()
    {
        var ports = Assemblies.Types(Assemblies.Application).Where(t => t.IsInterface && t.Namespace == Abstractions).ToList();
        Assert.NotEmpty(ports);
        var implementations = Assemblies.Types(Assemblies.Application)
            .Where(t => t.IsClass && ports.Any(p => p.IsAssignableFrom(t)))
            .Select(t => t.FullName)
            .ToList();
        Assert.Empty(implementations);
    }

    [Fact]
    public void Every_port_has_an_infrastructure_implementation()
    {
        var ports = Assemblies.Types(Assemblies.Application).Where(t => t.IsInterface && t.Namespace == Abstractions);
        Assert.All(ports, p => Assert.Contains(Assemblies.Types(Assemblies.Infrastructure), t => t.IsClass && p.IsAssignableFrom(t)));
    }

    [Fact]
    public void Validation_strategies_have_only_a_parameterless_constructor()
    {
        var strategies = Assemblies.Types(Assemblies.Application)
            .Where(t => t.IsClass && typeof(IBookingRequestValidationStrategy).IsAssignableFrom(t))
            .ToList();
        Assert.Equal(2, strategies.Count);
        Assert.All(strategies, s => Assert.Equal([0], s.GetConstructors().Select(c => c.GetParameters().Length)));
    }

    [Fact]
    public void Dtos_live_in_a_dtos_folder_and_services_never_declare_dtos() =>
        Assert.All(
            Assemblies.Types(Assemblies.Application).Where(t => t.Name.EndsWith("Request", StringComparison.Ordinal) && t.Name != "BookingRequest"
                && t.Name != "CheckoutSessionRequest"),
            t => Assert.EndsWith(".Dtos", t.Namespace, StringComparison.Ordinal));

    [Fact]
    public void Types_sit_in_namespaces_matching_their_layer() =>
        Assert.All(new[] { Assemblies.Domain, Assemblies.Application, Assemblies.Infrastructure }.SelectMany(Assemblies.Types).Where(t => t.IsPublic),
            t => Assert.StartsWith(t.Assembly.GetName().Name!, t.Namespace, StringComparison.Ordinal));
}

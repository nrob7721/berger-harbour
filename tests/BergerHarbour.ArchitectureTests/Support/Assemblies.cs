using System.Reflection;
using BergerHarbour.Application;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Infrastructure;

namespace BergerHarbour.ArchitectureTests.Support;

/// <summary>The solution's production assemblies, loaded by plain reflection.</summary>
public static class Assemblies
{
    public static readonly Assembly Domain = typeof(AggregateRoot).Assembly;
    public static readonly Assembly Application = typeof(ApplicationServiceCollectionExtensions).Assembly;
    public static readonly Assembly Infrastructure = typeof(InfrastructureServiceCollectionExtensions).Assembly;
    public static readonly Assembly PublicApi = typeof(PublicApi.Controllers.BoatsController).Assembly;
    public static readonly Assembly AdminApi = typeof(AdminApi.Controllers.BoatsController).Assembly;

    public static readonly IReadOnlyList<Assembly> Apis = [PublicApi, AdminApi];

    public static IEnumerable<Type> Types(Assembly assembly) => assembly.GetTypes().Where(t => !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false) && !t.Name.StartsWith('<'));

    public static IEnumerable<string> SolutionReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("BergerHarbour.", StringComparison.Ordinal));
}

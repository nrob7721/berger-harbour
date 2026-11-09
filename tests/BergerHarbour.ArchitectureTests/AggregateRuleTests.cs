using BergerHarbour.ArchitectureTests.Support;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.ArchitectureTests;

/// <summary>Every aggregate has a typed id and its own repository interface, implemented by exactly one Firestore repository.</summary>
public class AggregateRuleTests
{
    /// <summary>Aggregates whose identity isn't a <c>&lt;Name&gt;Id</c> (§7.9).</summary>
    private static readonly Dictionary<string, string?> IdentityExceptions = new()
    {
        ["AddonDefinition"] = "AddonId",
        ["EmailTemplate"] = "EmailTemplateKey",
        ["BusinessSettings"] = null,
    };

    public static TheoryData<string> Aggregates()
    {
        var data = new TheoryData<string>();
        foreach (var type in AggregateTypes())
        {
            data.Add(type.Name);
        }

        return data;
    }

    [Fact]
    public void There_are_ten_aggregates() =>
        Assert.Equal(
            ["AddonDefinition", "BlockedPeriod", "Boat", "BoatUnavailability", "Booking", "BookingDraft", "BusinessSettings", "Customer", "EmailTemplate", "Season"],
            AggregateTypes().Select(t => t.Name).Order());

    [Theory]
    [MemberData(nameof(Aggregates))]
    public void Each_aggregate_has_a_typed_id_in_its_own_namespace(string name)
    {
        var aggregate = AggregateTypes().Single(t => t.Name == name);
        var idName = IdentityExceptions.TryGetValue(name, out var special) ? special : name + "Id";
        if (idName is null)
        {
            return;
        }

        var id = Assemblies.Domain.GetType($"{aggregate.Namespace}.{idName}");
        Assert.NotNull(id);
        if (idName.EndsWith("Id", StringComparison.Ordinal))
        {
            Assert.True(id!.IsValueType, $"{idName} must be a readonly record struct.");
            Assert.NotNull(id.GetProperty("Value"));
        }
    }

    [Theory]
    [MemberData(nameof(Aggregates))]
    public void Each_aggregate_has_its_repository_interface_next_to_it_and_one_firestore_implementation(string name)
    {
        var aggregate = AggregateTypes().Single(t => t.Name == name);
        var repository = Assemblies.Domain.GetType($"{aggregate.Namespace}.I{name}Repository");
        Assert.NotNull(repository);
        Assert.True(repository!.IsInterface);
        var implementations = Assemblies.Types(Assemblies.Infrastructure).Where(t => t.IsClass && repository.IsAssignableFrom(t)).ToList();
        var implementation = Assert.Single(implementations);
        Assert.Equal($"Firestore{name}Repository", implementation.Name);
        Assert.Equal("BergerHarbour.Infrastructure.Firestore.Repositories", implementation.Namespace);
    }

    [Fact]
    public void Aggregates_have_no_public_setters()
    {
        var setters = AggregateTypes()
            .SelectMany(t => t.GetProperties().Where(p => p.SetMethod?.IsPublic == true).Select(p => $"{t.Name}.{p.Name}"))
            .ToList();
        Assert.Empty(setters);
    }

    private static IEnumerable<Type> AggregateTypes() =>
        Assemblies.Types(Assemblies.Domain).Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(AggregateRoot)));
}

using BergerHarbour.ArchitectureTests.Support;

namespace BergerHarbour.ArchitectureTests;

/// <summary>Domain → nothing; Application → Domain only; the APIs never reference each other.</summary>
public class LayerReferenceTests
{
    [Fact]
    public void Domain_references_no_other_project() => Assert.Empty(Assemblies.SolutionReferences(Assemblies.Domain));

    [Fact]
    public void Application_references_only_domain() => Assert.Equal(["BergerHarbour.Domain"], Assemblies.SolutionReferences(Assemblies.Application));

    [Fact]
    public void Neither_api_references_the_other()
    {
        Assert.DoesNotContain("BergerHarbour.AdminApi", Assemblies.SolutionReferences(Assemblies.PublicApi));
        Assert.DoesNotContain("BergerHarbour.PublicApi", Assemblies.SolutionReferences(Assemblies.AdminApi));
    }
}

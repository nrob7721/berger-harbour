namespace BergerHarbour.Infrastructure.IntegrationTests.Support;

[CollectionDefinition(Name)]
public sealed class EmulatorCollection : ICollectionFixture<EmulatorFixture>
{
    public const string Name = "emulator";
}

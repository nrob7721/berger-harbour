using BergerHarbour.Domain.Settings;
using BergerHarbour.Infrastructure.IntegrationTests.Support;

namespace BergerHarbour.Infrastructure.IntegrationTests.Firestore.Repositories;

[Collection(EmulatorCollection.Name)]
public class FirestoreEmailTemplateRepositoryTests(EmulatorFixture fx)
{
    [Fact]
    public async Task Every_template_is_seeded() =>
        Assert.Equal(Enum.GetValues<EmailTemplateKey>().Order(), (await fx.Get<IEmailTemplateRepository>().ListAsync()).Select(t => t.Key).Order());
}

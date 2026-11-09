using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Application.Tests.Fakes;

public sealed class InMemoryEmailTemplateRepository(InMemoryDatabase db) : IEmailTemplateRepository
{
    public Task<IReadOnlyList<EmailTemplate>> ListAsync(CancellationToken ct = default) => Task.FromResult(db.Templates.All);

    public Task<EmailTemplate?> GetAsync(EmailTemplateKey key, CancellationToken ct = default) => Task.FromResult(db.Templates.Get(key));

    public Task SaveAsync(EmailTemplate template, CancellationToken ct = default)
    {
        db.Templates.Save(template);
        return Task.CompletedTask;
    }
}

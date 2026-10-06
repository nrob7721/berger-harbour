namespace BergerHarbour.Domain.Settings;

public interface IEmailTemplateRepository
{
    Task<IReadOnlyList<EmailTemplate>> ListAsync(CancellationToken ct = default);

    Task<EmailTemplate?> GetAsync(EmailTemplateKey key, CancellationToken ct = default);

    Task SaveAsync(EmailTemplate template, CancellationToken ct = default);
}

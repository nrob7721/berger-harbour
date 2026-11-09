using BergerHarbour.Application.Abstractions;

namespace BergerHarbour.Infrastructure.IntegrationTests.Fakes;

public sealed class RecordingEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        lock (Sent)
        {
            Sent.Add(message);
        }

        return Task.CompletedTask;
    }
}

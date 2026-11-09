using BergerHarbour.Application.Abstractions;

namespace BergerHarbour.Application.Tests.Fakes;

public sealed class FakeEmailSender : IEmailSender
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

    public IReadOnlyList<EmailMessage> To(string address) => Sent.Where(m => m.To == address).ToList();
}

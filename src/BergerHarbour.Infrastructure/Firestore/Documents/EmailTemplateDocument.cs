using BergerHarbour.Domain.Settings;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

[FirestoreData]
public sealed class EmailTemplateDocument : IVersionedDocument
{
    [FirestoreProperty("subject")]
    public string Subject { get; set; } = string.Empty;

    [FirestoreProperty("htmlBody")]
    public string HtmlBody { get; set; } = string.Empty;

    [FirestoreProperty("version")]
    public int Version { get; set; }

    public static EmailTemplateDocument From(EmailTemplate t) => new() { Subject = t.Subject, HtmlBody = t.HtmlBody, Version = t.Version };

    public EmailTemplate ToDomain(string key) => EmailTemplate.Rehydrate(Enum.Parse<EmailTemplateKey>(key), Subject, HtmlBody, Version);
}

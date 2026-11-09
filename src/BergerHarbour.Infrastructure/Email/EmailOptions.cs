namespace BergerHarbour.Infrastructure.Email;

/// <summary>Email delivery settings (§9, §10.3).</summary>
public sealed class EmailOptions
{
    public EmailMode Mode { get; set; } = EmailMode.File;

    /// <summary>The business mailbox: SMTP login, From address and Reply-To.</summary>
    public string Sender { get; set; } = string.Empty;

    public string SenderName { get; set; } = "Berger Houseboats";

    /// <summary><c>[DEV] </c> in dev, empty in prod.</summary>
    public string SubjectPrefix { get; set; } = string.Empty;

    /// <summary>The Google app password of the sender mailbox.</summary>
    public string SmtpPassword { get; set; } = string.Empty;

    public string SmtpHost { get; set; } = "smtp.gmail.com";

    public int SmtpPort { get; set; } = 587;

    /// <summary>Where <c>File</c> mode writes emails (the console when empty).</summary>
    public string OutputDirectory { get; set; } = string.Empty;
}

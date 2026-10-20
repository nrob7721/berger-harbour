using BergerHarbour.Application.Abstractions;
using BergerHarbour.Infrastructure.Email;
using MimeKit;

namespace BergerHarbour.Infrastructure.IntegrationTests;

public class GmailSmtpEmailSenderTests
{
    private static SmtpOptions Options() => new()
    {
        SenderEmail = "info@bergerhouseboats.com.au",
        SenderName = "Berger Houseboats",
        Password = "app-password",
    };

    [Fact]
    public void Build_sets_from_reply_to_recipient_subject_and_html_body()
    {
        using var mime = GmailSmtpEmailSender.Build(Options(),
            new EmailMessage("jane@example.com", "Jane Citizen", "Booking confirmed", "<p>Hello</p>"));

        var from = Assert.IsType<MailboxAddress>(Assert.Single(mime.From));
        Assert.Equal("info@bergerhouseboats.com.au", from.Address);
        Assert.Equal("Berger Houseboats", from.Name);
        Assert.Equal("info@bergerhouseboats.com.au", Assert.IsType<MailboxAddress>(Assert.Single(mime.ReplyTo)).Address);
        var to = Assert.IsType<MailboxAddress>(Assert.Single(mime.To));
        Assert.Equal("jane@example.com", to.Address);
        Assert.Equal("Jane Citizen", to.Name);
        Assert.Equal("Booking confirmed", mime.Subject);
        Assert.Equal("<p>Hello</p>", mime.HtmlBody);
    }

    [Fact]
    public void Build_uses_the_address_as_the_recipient_name_when_none_is_given()
    {
        using var mime = GmailSmtpEmailSender.Build(Options(), new EmailMessage("jane@example.com", null, "Hi", "<p>x</p>"));

        Assert.Equal("jane@example.com", Assert.IsType<MailboxAddress>(Assert.Single(mime.To)).Name);
    }

    [Fact]
    public void Build_prepends_the_subject_prefix()
    {
        var options = Options();
        options.SubjectPrefix = "[DEV] ";

        using var mime = GmailSmtpEmailSender.Build(options, new EmailMessage("jane@example.com", null, "Booking confirmed", "<p>x</p>"));

        Assert.Equal("[DEV] Booking confirmed", mime.Subject);
    }

    [Fact]
    public void Options_are_valid_with_a_sender_address_and_password() =>
        Assert.Empty(Options().Validate());

    [Theory]
    [InlineData("", "app-password", "EMAIL_SENDER")]
    [InlineData("not-an-email", "app-password", "EMAIL_SENDER")]
    [InlineData("info@bergerhouseboats.com.au", "", "SMTP_PASSWORD")]
    public void Options_report_a_missing_or_invalid_sender_or_password(string email, string password, string expected)
    {
        var options = Options();
        options.SenderEmail = email;
        options.Password = password;

        Assert.Contains(options.Validate(), error => error.StartsWith(expected));
    }
}

using BergerHarbour.Application.Notifications;
using BergerHarbour.Application.Tests.Support;
using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Application.Tests.Notifications;

public class BookingNotifierTests
{
    private readonly ApplicationHarness h = new();

    [Fact]
    public async Task Staff_alert_renders_the_frequent_floater_notice_only_for_a_frequent_floater()
    {
        await h.BookOnlineAsync(start: "2027-03-08");
        Assert.DoesNotContain("Frequent Floater", h.EmailsTo(ApplicationHarness.StaffEmail).Single().HtmlBody, StringComparison.Ordinal);
        await h.BookOnlineAsync(start: "2027-05-03");
        var second = h.EmailsTo(ApplicationHarness.StaffEmail).Last();
        Assert.Contains("⭐ Frequent Floater: Jane Smith has 1 other booking within a year of this one.", second.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bank_placeholders_render_the_settings_or_empty()
    {
        var template = h.Db.Templates.Get(EmailTemplateKey.BookingConfirmed)!;
        template.Update(template.Subject, "Pay to {{AccountName}} BSB {{BSB}} account {{AccountNumber}} ref {{BookingReference}}.", EmailPlaceholders.Known);
        h.Db.Templates.Save(template);
        await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19"));
        Assert.Equal("Pay to  BSB  account  ref SMITH-2701150.", h.Emails.Sent.Single().HtmlBody);

        var settings = h.Db.Settings.Get("business")!;
        settings.Update(settings.DepositAmount, settings.MinimumLeadTimeDays, settings.BookingsOpenUntil, settings.StaffAlertEmail?.Value,
            settings.ContactPhone, settings.ContactEmail?.Value, settings.HireTermsUrl, "Berger Houseboats", "123456", "12345678");
        h.Db.Settings.Save(settings);
        await h.Staff.CreateAsync(h.StaffRequest("2027-02-15", "2027-02-19"));
        Assert.Equal("Pay to Berger Houseboats BSB 123-456 account 12345678 ref SMITH-2702150.", h.Emails.Sent.Last().HtmlBody);
    }

    [Fact]
    public async Task Values_are_html_encoded_and_formatted_the_australian_way()
    {
        var template = h.Db.Templates.Get(EmailTemplateKey.BookingConfirmed)!;
        template.Update("{{BoatName}}", "{{CustomerName}}|{{CheckInDate}}|{{CheckInTime}}|{{CheckOutTime}}|{{TotalPrice}}", EmailPlaceholders.Known);
        h.Db.Templates.Save(template);
        await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19", lastName: "O'Brien <b>", hire: 12345));
        Assert.Equal("John O&#39;Brien &lt;b&gt;|15/01/2027|11am – 2pm AEDT/NSW|8am AEDT/NSW|$12,345.00", h.Emails.Sent.Single().HtmlBody);
    }

    [Fact]
    public void Unknown_placeholders_are_rejected()
    {
        var template = h.Db.Templates.Get(EmailTemplateKey.PaymentDue)!;
        var e = Assert.Throws<Domain.Shared.DomainValidationException>(() => template.Update("Hi", "{{CustomerComments}}", EmailPlaceholders.Known));
        Assert.True(e.Errors.Has("htmlBody"));
    }
}

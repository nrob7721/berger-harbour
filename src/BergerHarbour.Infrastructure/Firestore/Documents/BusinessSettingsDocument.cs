using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

[FirestoreData]
public sealed class BusinessSettingsDocument : IVersionedDocument
{
    [FirestoreProperty("depositAmountCents")]
    public long DepositAmountCents { get; set; }

    [FirestoreProperty("minimumLeadTimeDays")]
    public int MinimumLeadTimeDays { get; set; }

    [FirestoreProperty("bookingsOpenUntil")]
    public string BookingsOpenUntil { get; set; } = string.Empty;

    [FirestoreProperty("staffAlertEmail")]
    public string? StaffAlertEmail { get; set; }

    [FirestoreProperty("contactPhone")]
    public string? ContactPhone { get; set; }

    [FirestoreProperty("contactEmail")]
    public string? ContactEmail { get; set; }

    [FirestoreProperty("hireTermsUrl")]
    public string HireTermsUrl { get; set; } = string.Empty;

    [FirestoreProperty("accountName")]
    public string? AccountName { get; set; }

    [FirestoreProperty("bsb")]
    public string? Bsb { get; set; }

    [FirestoreProperty("accountNumber")]
    public string? AccountNumber { get; set; }

    [FirestoreProperty("version")]
    public int Version { get; set; }

    public static BusinessSettingsDocument From(BusinessSettings s) => new()
    {
        DepositAmountCents = FirestoreValues.Cents(s.DepositAmount),
        MinimumLeadTimeDays = s.MinimumLeadTimeDays,
        BookingsOpenUntil = FirestoreValues.Date(s.BookingsOpenUntil),
        StaffAlertEmail = s.StaffAlertEmail?.Value,
        ContactPhone = s.ContactPhone,
        ContactEmail = s.ContactEmail?.Value,
        HireTermsUrl = s.HireTermsUrl,
        AccountName = s.AccountName,
        Bsb = s.Bsb,
        AccountNumber = s.AccountNumber,
        Version = s.Version,
    };

    public BusinessSettings ToDomain() => BusinessSettings.Rehydrate(
        FirestoreValues.Money(DepositAmountCents), MinimumLeadTimeDays, FirestoreValues.Date(BookingsOpenUntil),
        StaffAlertEmail is null ? null : EmailAddress.Create(StaffAlertEmail), ContactPhone, ContactEmail is null ? null : EmailAddress.Create(ContactEmail),
        HireTermsUrl, AccountName, Bsb, AccountNumber, Version);
}

namespace BergerHarbour.Application.Settings.Dtos;

/// <summary>The General settings (also the PUT body, with the version edited).</summary>
public sealed record BusinessSettingsDto(
    decimal DepositAmount,
    int MinimumLeadTimeDays,
    DateOnly BookingsOpenUntil,
    string? StaffAlertEmail,
    string? ContactPhone,
    string? ContactEmail,
    string HireTermsUrl,
    string? AccountName,
    string? Bsb,
    string? AccountNumber,
    int Version);

using BergerHarbour.Application.Bookings;
using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Application.Settings;

public sealed record BusinessSettingsDto(
    int Version,
    decimal DepositAmount,
    int MinimumLeadTimeDays,
    DateOnly BookingsOpenUntil,
    string StaffAlertEmail,
    string ContactPhone,
    string ContactEmail,
    string HireTermsUrl);

public sealed record SeasonRangeDto(string StartDayMonth, string EndDayMonth);

public sealed record SeasonDto(string Id, int Version, string Name, bool IsDefault, IReadOnlyList<SeasonRangeDto> Ranges);

public sealed record SeasonRequest(int Version, string Name, IReadOnlyList<SeasonRangeDto>? Ranges);

public sealed record BlockedPeriodDto(
    string Id,
    int Version,
    string Name,
    DateOnly FirstNight,
    DateOnly LastNight,
    bool UsesExtendedPaymentSchedule);

public sealed record BlockedPeriodRequest(
    int Version,
    string Name,
    DateOnly FirstNight,
    DateOnly LastNight,
    bool UsesExtendedPaymentSchedule);

public sealed record AddonDto(
    string Id,
    int Version,
    string Name,
    string? Description,
    bool PriceOnRequest,
    AddonPricesDto? Prices,
    bool QuantityApplies,
    bool IsActive);

public sealed record AddonRequest(
    int Version,
    string Name,
    string? Description,
    bool PriceOnRequest,
    AddonPricesDto? Prices,
    bool QuantityApplies,
    bool IsActive);

public sealed record EmailTemplateDto(EmailTemplateKey Key, int Version, string Subject, string HtmlBody);

public sealed record EmailTemplateRequest(int Version, string Subject, string HtmlBody);

public sealed record EmailTemplatesDto(IReadOnlyList<EmailTemplateDto> Templates, IReadOnlyList<string> Placeholders);

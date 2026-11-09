using BergerHarbour.Application.Settings.Dtos;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Settings;

/// <summary>General settings (<c>BusinessSettingsController</c>).</summary>
public sealed class BusinessSettingsService
{
    private readonly IBusinessSettingsRepository settings;

    public BusinessSettingsService(IBusinessSettingsRepository settings)
    {
        this.settings = settings;
    }

    public async Task<BusinessSettingsDto> GetAsync(CancellationToken ct = default) => ToDto(await LoadAsync(ct));

    public async Task<BusinessSettingsDto> UpdateAsync(BusinessSettingsDto request, CancellationToken ct = default)
    {
        var current = await LoadAsync(ct);
        current.EnsureVersion(request.Version);
        current.Update(request.DepositAmount, request.MinimumLeadTimeDays, request.BookingsOpenUntil, request.StaffAlertEmail, request.ContactPhone,
            request.ContactEmail, request.HireTermsUrl, request.AccountName, request.Bsb, request.AccountNumber);
        await settings.SaveAsync(current, ct);
        return ToDto(current);
    }

    private static BusinessSettingsDto ToDto(BusinessSettings s) =>
        new(s.DepositAmount, s.MinimumLeadTimeDays, s.BookingsOpenUntil, s.StaffAlertEmail?.Value, s.ContactPhone, s.ContactEmail?.Value, s.HireTermsUrl,
            s.AccountName, s.Bsb, s.AccountNumber, s.Version);

    private async Task<BusinessSettings> LoadAsync(CancellationToken ct) =>
        await settings.GetAsync(ct) ?? throw new NotFoundException("Business settings are missing. Run the seed first.");
}

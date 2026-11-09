using BergerHarbour.Application.Boats;
using BergerHarbour.Application.BookingDrafts;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Customers;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Application.Payments;
using BergerHarbour.Application.Seed;
using BergerHarbour.Application.Settings;
using BergerHarbour.Application.Unavailabilities;
using BergerHarbour.Domain.Bookings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Application;

/// <summary>
/// Registers the domain services, collaborators and application services. Each API registers its own booking request
/// validation strategy (online in the public API, internal in the admin API).
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddBergerApplication(this IServiceCollection services)
    {
        // Domain services (stateless).
        services.AddSingleton<BookingAvailabilityService>();
        services.AddSingleton<PricingService>();
        services.AddSingleton<AddonCapacityService>();
        services.AddSingleton<StayPeriodFactory>();
        services.AddSingleton<OnlineStayPolicy>();
        services.AddSingleton<FrequentFloaterService>();
        services.AddSingleton(sp => new PaymentScheduleService(sp.GetRequiredService<IOptions<NotificationOptions>>().Value.ToScheduleOptions()));

        // Collaborators.
        services.AddSingleton<EmailRenderer>();
        services.AddSingleton<PaymentLinkTokens>();
        services.AddSingleton<StaffBookingWarnings>();
        services.AddScoped<ReferenceDataLoader>();
        services.AddScoped<CustomerRegistry>();
        services.AddScoped<BookingNotifier>();
        services.AddScoped<OnlineBookingPreparer>();

        // Application services, one per controller.
        services.AddScoped<BoatCatalogueService>();
        services.AddScoped<OnlineQuoteService>();
        services.AddScoped<AvailabilityService>();
        services.AddScoped<AddonAvailabilityService>();
        services.AddScoped<BookingDraftService>();
        services.AddScoped<OnlineBookingService>();
        services.AddScoped<PaymentLinkService>();
        services.AddScoped<StripeWebhookService>();
        services.AddScoped<StaffBookingService>();
        services.AddScoped<BoatService>();
        services.AddScoped<BoatUnavailabilityService>();
        services.AddScoped<CustomerService>();
        services.AddScoped<BusinessSettingsService>();
        services.AddScoped<SeasonService>();
        services.AddScoped<BlockedPeriodService>();
        services.AddScoped<AddonDefinitionService>();
        services.AddScoped<EmailTemplateService>();
        services.AddScoped<NotificationJobService>();
        services.AddScoped<SeedService>();
        return services;
    }
}

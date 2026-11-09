using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Boats;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Application.Catalogue;
using BergerHarbour.Application.Common;
using BergerHarbour.Application.Customers;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Application.Seed;
using BergerHarbour.Application.Settings;
using BergerHarbour.Domain.Bookings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Application;

public static class DependencyInjection
{
    /// <summary>Use cases shared by both APIs. Each API registers its own <see cref="IBookingRequestValidationStrategy"/>.</summary>
    public static IServiceCollection AddApplicationCore(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(sp => new PaymentScheduleService(sp.GetRequiredService<IOptions<NotificationOptions>>().Value.ToScheduleOptions()));
        services.AddScoped<ReferenceDataLoader>();
        services.AddScoped<CustomerRegistry>();
        services.AddScoped<BookingNotifier>();
        services.AddScoped<PublicCatalogueService>();
        return services;
    }

    public static IServiceCollection AddPublicUseCases(this IServiceCollection services)
    {
        services.AddScoped<IBookingRequestValidationStrategy, OnlineBookingValidationStrategy>();
        services.AddScoped<OnlineBookingService>();
        services.AddScoped<PaymentLinkService>();
        services.AddScoped<StripeWebhookService>();
        return services;
    }

    public static IServiceCollection AddAdminUseCases(this IServiceCollection services)
    {
        services.AddScoped<IBookingRequestValidationStrategy, InternalBookingValidationStrategy>();
        services.AddScoped<AdminBookingService>();
        services.AddScoped<CustomerAdminService>();
        services.AddScoped<FleetService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<NotificationJob>();
        services.AddScoped<SeedService>();
        return services;
    }
}

using BergerHarbour.Application.Boats;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Common;
using BergerHarbour.Application.Customers;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Application.Payments;
using BergerHarbour.Application.Seeding;
using BergerHarbour.Application.Settings;
using BergerHarbour.Domain.Bookings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the use cases. Each API also registers its own <see cref="Bookings.Validation.IBookingRequestValidationStrategy"/>.
    /// </summary>
    public static IServiceCollection AddBergerHarbourApplication(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<NotificationOptions>>().Value;
            return new PaymentScheduleService(new PaymentScheduleOffsets(o.BalanceDueBeforeHire,
                o.ExtendedFirstInstalmentBeforeHire, o.ExtendedFinalInstalmentBeforeHire, o.PaymentReminderBeforeDue));
        });
        services.AddSingleton<PaymentLinkTokens>();
        services.AddScoped<BookingCatalogueLoader>();
        services.AddScoped<CustomerService>();
        services.AddScoped<CustomerQueries>();
        services.AddScoped<NotificationService>();
        services.AddScoped<NotificationJob>();
        services.AddScoped<OnlineBookingService>();
        services.AddScoped<AdminBookingService>();
        services.AddScoped<PaymentLinkService>();
        services.AddScoped<StripeWebhookHandler>();
        services.AddScoped<FleetService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<SeedService>();
        return services;
    }
}

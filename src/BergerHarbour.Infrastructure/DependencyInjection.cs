using BergerHarbour.Application;
using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Common;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Unavailabilities;
using BergerHarbour.Infrastructure.Brevo;
using BergerHarbour.Infrastructure.Cloudflare;
using BergerHarbour.Infrastructure.Email;
using BergerHarbour.Infrastructure.Firestore;
using BergerHarbour.Infrastructure.Google;
using BergerHarbour.Infrastructure.Stripe;
using BergerHarbour.Infrastructure.Time;
using BergerHarbour.Infrastructure.Turnstile;
using EmulatorDetection = Google.Api.Gax.EmulatorDetection;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IClock = BergerHarbour.Application.Abstractions.IClock;
using SystemClock = BergerHarbour.Infrastructure.Time.SystemClock;

namespace BergerHarbour.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence, external services and options. Secrets come from flat environment variables
    /// (injected from Secret Manager on Cloud Run); timing comes from Notifications__* and Booking__*.
    /// </summary>
    public static IServiceCollection AddBergerHarbourInfrastructure(this IServiceCollection services,
        IConfiguration config, bool isDevelopment)
    {
        services.AddBergerHarbourApplication();

        var notifications = services.AddOptions<NotificationOptions>().Bind(config.GetSection(NotificationOptions.Section))
            .Validate(o => !o.Validate().Any(), "Invalid Notifications__* settings");
        var booking = services.AddOptions<BookingOptions>().Bind(config.GetSection(BookingOptions.Section))
            .Validate(o => !o.Validate().Any(), "Invalid Booking__* settings");
        var paymentLinks = services.AddOptions<PaymentLinkOptions>().Configure(o =>
        {
            o.PublicBookingBaseUrl = config["PUBLIC_BOOKING_BASE_URL"] ?? o.PublicBookingBaseUrl;
            o.Secret = config["PAYMENT_LINK_SECRET"] ?? string.Empty;
        }).Validate(o => !o.Validate().Any(), "PUBLIC_BOOKING_BASE_URL / PAYMENT_LINK_SECRET are invalid");
        if (!IsOpenApiGeneration)
        {
            // Fail fast on bad timing or link settings when the service starts.
            notifications.ValidateOnStart();
            booking.ValidateOnStart();
            paymentLinks.ValidateOnStart();
        }
        services.Configure<StripeOptions>(o =>
        {
            o.SecretKey = config["STRIPE_SECRET_KEY"] ?? string.Empty;
            o.WebhookSecret = config["STRIPE_WEBHOOK_SECRET"] ?? string.Empty;
        });
        services.Configure<TurnstileOptions>(o => o.SecretKey = config["TURNSTILE_SECRET"] ?? string.Empty);
        services.Configure<BrevoOptions>(o =>
        {
            o.ApiKey = config["BREVO_API_KEY"] ?? string.Empty;
            o.SenderEmail = config["EMAIL_SENDER"] ?? o.SenderEmail;
        });
        services.Configure<CloudflareAccessOptions>(o =>
        {
            o.TeamDomain = config["CF_ACCESS_TEAM_DOMAIN"] ?? string.Empty;
            o.Audience = config["CF_ACCESS_AUD"] ?? string.Empty;
            o.AllowedEmails = config["ADMIN_ALLOWED_EMAILS"] ?? string.Empty;
            o.DevBypassEmail = isDevelopment ? config["DEV_ADMIN_EMAIL"] : null;
        });
        services.Configure<SchedulerOidcOptions>(o =>
        {
            o.ServiceAccountEmail = config["SCHEDULER_SERVICE_ACCOUNT_EMAIL"] ?? string.Empty;
            o.Audience = config["SCHEDULER_AUDIENCE"] ?? string.Empty;
            o.DevBypass = isDevelopment && config.GetValue("DEV_JOBS_BYPASS", false);
        });

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(_ => CreateFirestore(config));
        services.AddSingleton<IBoatRepository, FirestoreBoatRepository>();
        services.AddSingleton<IBookingRepository, FirestoreBookingRepository>();
        services.AddSingleton<ICustomerRepository, FirestoreCustomerRepository>();
        services.AddSingleton<IBoatUnavailabilityRepository, FirestoreBoatUnavailabilityRepository>();
        services.AddSingleton<ISeasonRepository, FirestoreSeasonRepository>();
        services.AddSingleton<IBlockedPeriodRepository, FirestoreBlockedPeriodRepository>();
        services.AddSingleton<IAddonDefinitionRepository, FirestoreAddonDefinitionRepository>();
        services.AddSingleton<IEmailTemplateRepository, FirestoreEmailTemplateRepository>();
        services.AddSingleton<IBusinessSettingsRepository, FirestoreBusinessSettingsRepository>();
        services.AddSingleton<IBoatScheduleLock, FirestoreBoatScheduleLock>();
        services.AddSingleton<IBookingReferenceGenerator, FirestoreBookingReferenceGenerator>();

        services.AddSingleton<IPaymentGateway, StripePaymentGateway>();
        services.AddSingleton<StripeWebhookParser>();
        services.AddHttpClient<ITurnstileVerifier, TurnstileVerifier>(c => c.BaseAddress = new Uri(TurnstileVerifier.BaseAddress));
        services.AddHttpClient<CloudflareAccessValidator>();
        services.AddSingleton<CloudflareAccessValidator>(sp => new CloudflareAccessValidator(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(CloudflareAccessValidator)),
            sp.GetRequiredService<IOptions<CloudflareAccessOptions>>(),
            sp.GetRequiredService<ILogger<CloudflareAccessValidator>>()));
        services.AddSingleton<SchedulerOidcValidator>();

        var emailMode = config["EMAIL_MODE"] ?? (isDevelopment ? "File" : "Brevo");
        if (string.Equals(emailMode, "File", StringComparison.OrdinalIgnoreCase))
        {
            var folder = config["EMAIL_OUTPUT_DIR"] ?? Path.Combine(Path.GetTempPath(), "berger-harbour-emails");
            services.AddSingleton<IEmailSender>(sp => new FileEmailSender(folder, sp.GetRequiredService<ILogger<FileEmailSender>>()));
        }
        else
        {
            services.AddHttpClient<IEmailSender, BrevoEmailSender>(c =>
            {
                c.BaseAddress = new Uri(BrevoEmailSender.BaseAddress);
                c.Timeout = TimeSpan.FromSeconds(20);
            });
        }

        return services;
    }

    /// <summary>True while the build is generating the OpenAPI document (the host is started without secrets).</summary>
    public static bool IsOpenApiGeneration =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    /// <summary>Uses the emulator when FIRESTORE_EMULATOR_HOST is set.</summary>
    public static FirestoreDb CreateFirestore(IConfiguration config)
    {
        var projectId = config["FIRESTORE_PROJECT_ID"] ?? config["GOOGLE_CLOUD_PROJECT"]
                        ?? throw new InvalidOperationException("Set FIRESTORE_PROJECT_ID (or GOOGLE_CLOUD_PROJECT).");
        var emulatorHost = config["FIRESTORE_EMULATOR_HOST"];
        if (!string.IsNullOrEmpty(emulatorHost) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
        {
            Environment.SetEnvironmentVariable("FIRESTORE_EMULATOR_HOST", emulatorHost);
        }

        return new FirestoreDbBuilder
        {
            ProjectId = projectId,
            EmulatorDetection = EmulatorDetection.EmulatorOrProduction,
        }.Build();
    }
}

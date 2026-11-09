using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Application.Payments;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Unavailabilities;
using BergerHarbour.Infrastructure.Cloudflare;
using BergerHarbour.Infrastructure.Email;
using BergerHarbour.Infrastructure.Firestore;
using BergerHarbour.Infrastructure.Firestore.Repositories;
using BergerHarbour.Infrastructure.Stripe;
using BergerHarbour.Infrastructure.Time;
using BergerHarbour.Infrastructure.Turnstile;
using BergerHarbour.Infrastructure.Web;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BergerHarbour.Infrastructure;

/// <summary>Registers the port implementations: Firestore, Stripe, email, Turnstile, Access, the clock, and the options.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddBergerInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var local = StartupConfiguration.IsLocal(environment);
        services.Configure<BookingOptions>(configuration.GetSection(BookingOptions.Section));
        services.Configure<NotificationOptions>(configuration.GetSection(NotificationOptions.Section));
        services.Configure<PaymentLinkOptions>(o =>
        {
            o.PublicBookingBaseUrl = configuration[ConfigKeys.PublicBookingBaseUrl] ?? string.Empty;
            o.Secret = configuration[ConfigKeys.PaymentLinkSecret] ?? string.Empty;
        });
        services.Configure<StripeOptions>(o =>
        {
            o.SecretKey = configuration[ConfigKeys.StripeSecretKey] ?? string.Empty;
            o.WebhookSecret = configuration[ConfigKeys.StripeWebhookSecret] ?? string.Empty;
        });
        services.Configure<EmailOptions>(o =>
        {
            o.Mode = Enum.TryParse<EmailMode>(configuration[ConfigKeys.EmailMode], true, out var mode) ? mode : EmailMode.File;
            o.Sender = configuration[ConfigKeys.EmailSender] ?? string.Empty;
            o.SenderName = configuration[ConfigKeys.EmailSenderName] ?? "Berger Houseboats";
            o.SubjectPrefix = configuration[ConfigKeys.EmailSubjectPrefix] ?? string.Empty;
            o.SmtpPassword = configuration[ConfigKeys.SmtpPassword] ?? string.Empty;
            o.OutputDirectory = configuration[ConfigKeys.EmailOutputDir] ?? string.Empty;
        });
        services.Configure<TurnstileOptions>(o =>
        {
            var secret = configuration[ConfigKeys.TurnstileSecret];
            o.Secret = string.IsNullOrWhiteSpace(secret) && local ? TurnstileOptions.AlwaysPassTestSecret : secret ?? string.Empty;
        });
        services.Configure<CloudflareAccessOptions>(o =>
        {
            o.TeamDomain = configuration[ConfigKeys.CfAccessTeamDomain] ?? string.Empty;
            o.Audience = configuration[ConfigKeys.CfAccessAud] ?? string.Empty;
            o.AllowedEmails = configuration[ConfigKeys.AdminAllowedEmails] ?? string.Empty;
        });
        services.Configure<SchedulerOidcOptions>(o =>
        {
            o.ServiceAccountEmail = configuration[ConfigKeys.SchedulerServiceAccountEmail] ?? string.Empty;
            o.Audience = configuration[ConfigKeys.SchedulerAudience] ?? string.Empty;
        });

        services.AddSingleton<IClock, SystemClock>();
        services.AddFirestore(configuration[ConfigKeys.FirestoreProjectId] ?? string.Empty);
        services.AddSingleton<IPaymentGateway, StripePaymentGateway>();
        var emailMode = Enum.TryParse<EmailMode>(configuration[ConfigKeys.EmailMode], true, out var m) ? m : EmailMode.File;
        if (emailMode == EmailMode.Smtp)
        {
            services.AddSingleton<IEmailSender, GmailSmtpEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, FileEmailSender>();
        }

        services.AddHttpClient<ITurnstileVerifier, CloudflareTurnstileVerifier>(c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpClient(nameof(CloudflareAccessJwtValidator), c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton(sp => ActivatorUtilities.CreateInstance<CloudflareAccessJwtValidator>(
            sp, sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(CloudflareAccessJwtValidator))));
        services.AddSingleton<GoogleOidcTokenValidator>();
        return services;
    }

    /// <summary>
    /// Firestore (Native mode), against the emulator when <c>FIRESTORE_EMULATOR_HOST</c> is set. Every Firestore-specific
    /// detail stays here, so the database can be swapped.
    /// </summary>
    public static IServiceCollection AddFirestore(this IServiceCollection services, string projectId)
    {
        services.AddSingleton(_ => new FirestoreDbBuilder { ProjectId = projectId, EmulatorDetection = Google.Api.Gax.EmulatorDetection.EmulatorOrProduction }
            .Build());
        services.AddSingleton(sp => new VersionedDocuments(sp.GetRequiredService<FirestoreDb>()));
        services.AddSingleton<IBoatRepository>(sp => new FirestoreBoatRepository(Db(sp), Docs(sp)));
        services.AddSingleton<IBookingRepository>(sp => new FirestoreBookingRepository(Db(sp), Docs(sp)));
        services.AddSingleton<IBookingDraftRepository>(sp => new FirestoreBookingDraftRepository(Db(sp), Docs(sp)));
        services.AddSingleton<ICustomerRepository>(sp => new FirestoreCustomerRepository(Db(sp), Docs(sp)));
        services.AddSingleton<IBoatUnavailabilityRepository>(sp => new FirestoreBoatUnavailabilityRepository(Db(sp), Docs(sp)));
        services.AddSingleton<ISeasonRepository>(sp => new FirestoreSeasonRepository(Db(sp), Docs(sp)));
        services.AddSingleton<IBlockedPeriodRepository>(sp => new FirestoreBlockedPeriodRepository(Db(sp), Docs(sp)));
        services.AddSingleton<IAddonDefinitionRepository>(sp => new FirestoreAddonDefinitionRepository(Db(sp), Docs(sp)));
        services.AddSingleton<IEmailTemplateRepository>(sp => new FirestoreEmailTemplateRepository(Db(sp), Docs(sp)));
        services.AddSingleton<IBusinessSettingsRepository>(sp => new FirestoreBusinessSettingsRepository(Db(sp), Docs(sp)));
        services.AddSingleton<IBoatScheduleLock>(sp => new FirestoreBoatScheduleLock(Db(sp)));
        services.AddSingleton<IBookingReferenceGenerator>(sp => new FirestoreBookingReferenceGenerator(Db(sp)));
        return services;
    }

    private static FirestoreDb Db(IServiceProvider sp) => sp.GetRequiredService<FirestoreDb>();

    private static VersionedDocuments Docs(IServiceProvider sp) => sp.GetRequiredService<VersionedDocuments>();
}

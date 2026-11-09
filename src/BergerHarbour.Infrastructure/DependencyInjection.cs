using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Common;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Unavailabilities;
using BergerHarbour.Infrastructure.Email;
using BergerHarbour.Infrastructure.Firestore;
using BergerHarbour.Infrastructure.Web;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BergerHarbour.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Firestore, email and payment-link tokens: everything both APIs need from Infrastructure.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config, IHostEnvironment env,
        ApiHosting.StartupChecks checks)
    {
        var local = env.IsLocal();
        var projectId = local ? config[ConfigKeys.FirestoreProjectId] ?? "demo-berger-harbour" : checks.Require(ConfigKeys.FirestoreProjectId);
        services.AddFirestore(projectId);

        var linkSecret = local ? config[ConfigKeys.PaymentLinkSecret] ?? "local-payment-link-secret" : checks.Require(ConfigKeys.PaymentLinkSecret);
        if (!local && linkSecret.Length is > 0 and < 32) checks.Add($"{ConfigKeys.PaymentLinkSecret} must be at least 32 characters.");
        services.AddSingleton(new PaymentLinkTokens(linkSecret));

        var emailMode = ApiHosting.ParseEmailMode(config);
        services.Configure<EmailOptions>(o =>
        {
            o.Mode = emailMode;
            o.Sender = config[ConfigKeys.EmailSender] ?? "";
            o.SenderName = config[ConfigKeys.EmailSenderName] is { Length: > 0 } n ? n : "Berger Houseboats";
            o.SmtpPassword = config[ConfigKeys.SmtpPassword] ?? "";
            o.SubjectPrefix = config[ConfigKeys.EmailSubjectPrefix] ?? "";
            o.OutputDirectory = config[ConfigKeys.EmailOutputDir] is { Length: > 0 } d ? d : ".dev-emails";
        });
        checks.Add(new EmailOptions
        {
            Mode = emailMode, Sender = config[ConfigKeys.EmailSender] ?? "", SmtpPassword = config[ConfigKeys.SmtpPassword] ?? "",
        }.Validate());
        if (emailMode == EmailMode.File) services.AddSingleton<IEmailSender, FileEmailSender>();
        else services.AddSingleton<IEmailSender, SmtpEmailSender>();
        return services;
    }

    /// <summary>The Firestore client is created on first use, so building the app (and the OpenAPI document) touches nothing.</summary>
    public static IServiceCollection AddFirestore(this IServiceCollection services, string projectId)
    {
        services.AddSingleton(_ => new FirestoreDbBuilder { ProjectId = projectId, EmulatorDetection = global::Google.Api.Gax.EmulatorDetection.EmulatorOrProduction }.Build());
        services.AddSingleton<FirestoreStore>();
        services.AddSingleton<IBoatRepository, FirestoreBoatRepository>();
        services.AddSingleton<IBookingRepository, FirestoreBookingRepository>();
        services.AddSingleton<IBookingDraftRepository, FirestoreBookingDraftRepository>();
        services.AddSingleton<ICustomerRepository, FirestoreCustomerRepository>();
        services.AddSingleton<IBoatUnavailabilityRepository, FirestoreBoatUnavailabilityRepository>();
        services.AddSingleton<ISeasonRepository, FirestoreSeasonRepository>();
        services.AddSingleton<IBlockedPeriodRepository, FirestoreBlockedPeriodRepository>();
        services.AddSingleton<IAddonDefinitionRepository, FirestoreAddonDefinitionRepository>();
        services.AddSingleton<IEmailTemplateRepository, FirestoreEmailTemplateRepository>();
        services.AddSingleton<IBusinessSettingsRepository, FirestoreBusinessSettingsRepository>();
        services.AddSingleton<IBoatScheduleLock, FirestoreBoatScheduleLock>();
        services.AddSingleton<IBookingReferenceGenerator, FirestoreBookingReferenceGenerator>();
        services.AddSingleton<IClock, SystemClock>();
        return services;
    }
}

using System.Globalization;
using System.Reflection;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BergerHarbour.Infrastructure.Web;

/// <summary>
/// Fail-fast configuration checks: a service refuses to start without its required settings, so an environment can
/// never fall back to another environment's frontend, and Local-only bypasses can't be enabled elsewhere.
/// </summary>
public static class StartupConfiguration
{
    /// <summary>True while <c>dotnet build</c> runs the app to write its OpenAPI document; no checks or connections then.</summary>
    public static bool IsGeneratingOpenApiDocument =>
        Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    /// <summary>True for <c>dotnet run -- seed</c>, which only needs the Firestore project.</summary>
    public static bool IsSeedCommand =>
        Environment.GetCommandLineArgs().Skip(1).Any(a => a.Equals("seed", StringComparison.OrdinalIgnoreCase));

    public static bool IsLocal(IHostEnvironment environment) => environment.IsEnvironment(ConfigKeys.LocalEnvironment);

    public static int RateLimitPerMinute(IConfiguration configuration, IHostEnvironment environment)
    {
        var text = configuration[ConfigKeys.RateLimitPerMinute];
        if (string.IsNullOrWhiteSpace(text))
        {
            return IsLocal(environment) ? 0 : 60;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var limit)
            ? limit
            : throw new InvalidOperationException($"{ConfigKeys.RateLimitPerMinute} must be a whole number ≥ 0 (0 disables the limit), not '{text}'.");
    }

    public static void Validate(IConfiguration configuration, IHostEnvironment environment, ApiKind api)
    {
        var problems = new List<string>();
        var local = IsLocal(environment);
        if (IsSeedCommand)
        {
            if (string.IsNullOrWhiteSpace(configuration[ConfigKeys.FirestoreProjectId]))
            {
                throw new InvalidOperationException($"The seed can't start: {ConfigKeys.FirestoreProjectId} is required.");
            }

            return;
        }

        void Require(string key)
        {
            if (string.IsNullOrWhiteSpace(configuration[key]))
            {
                problems.Add($"{key} is required.");
            }
        }

        void RequireOutsideLocal(string key)
        {
            if (!local)
            {
                Require(key);
            }
        }

        Require(ConfigKeys.PublicBookingBaseUrl);
        Require(ConfigKeys.CorsAllowedOrigin);
        Require(ConfigKeys.FirestoreProjectId);
        RequireOutsideLocal(ConfigKeys.EdgeProxySecret);
        var origin = configuration[ConfigKeys.CorsAllowedOrigin];
        if (!string.IsNullOrWhiteSpace(origin) && (origin.Contains('*') || !Uri.TryCreate(origin, UriKind.Absolute, out _)))
        {
            problems.Add($"{ConfigKeys.CorsAllowedOrigin} must be one absolute origin, never a wildcard.");
        }

        if ((configuration[ConfigKeys.PaymentLinkSecret] ?? string.Empty).Length < 32)
        {
            problems.Add($"{ConfigKeys.PaymentLinkSecret} must be at least 32 characters.");
        }

        if (Enum.TryParse<Email.EmailMode>(configuration[ConfigKeys.EmailMode], true, out var mode) && mode == Email.EmailMode.Smtp)
        {
            Require(ConfigKeys.EmailSender);
            Require(ConfigKeys.SmtpPassword);
        }
        else if (!string.IsNullOrWhiteSpace(configuration[ConfigKeys.EmailMode]) && !Enum.TryParse<Email.EmailMode>(configuration[ConfigKeys.EmailMode], true, out _))
        {
            problems.Add($"{ConfigKeys.EmailMode} must be Smtp or File.");
        }

        if (!local)
        {
            foreach (var key in new[] { ConfigKeys.DevAdminEmail, ConfigKeys.DevJobsBypass })
            {
                if (!string.IsNullOrWhiteSpace(configuration[key]))
                {
                    problems.Add($"{key} is only allowed when ASPNETCORE_ENVIRONMENT=Local.");
                }
            }
        }

        if (api == ApiKind.Public)
        {
            RequireOutsideLocal(ConfigKeys.StripeSecretKey);
            RequireOutsideLocal(ConfigKeys.StripeWebhookSecret);
            RequireOutsideLocal(ConfigKeys.TurnstileSecret);
            try
            {
                if (RateLimitPerMinute(configuration, environment) < 0)
                {
                    problems.Add($"{ConfigKeys.RateLimitPerMinute} must be ≥ 0.");
                }
            }
            catch (InvalidOperationException e)
            {
                problems.Add(e.Message);
            }
        }
        else
        {
            if (!local || string.IsNullOrWhiteSpace(configuration[ConfigKeys.DevAdminEmail]))
            {
                Require(ConfigKeys.CfAccessTeamDomain);
                Require(ConfigKeys.CfAccessAud);
                Require(ConfigKeys.AdminAllowedEmails);
            }

            if (!local || !IsTrue(configuration[ConfigKeys.DevJobsBypass]))
            {
                Require(ConfigKeys.SchedulerServiceAccountEmail);
                Require(ConfigKeys.SchedulerAudience);
            }
        }

        ValidateTimings(configuration, problems);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException("The service can't start:\n- " + string.Join("\n- ", problems));
        }
    }

    public static bool IsTrue(string? value) => bool.TryParse(value, out var b) && b;

    private static void ValidateTimings(IConfiguration configuration, List<string> problems)
    {
        foreach (var section in new[] { BookingOptions.Section, NotificationOptions.Section })
        {
            foreach (var child in configuration.GetSection(section).GetChildren())
            {
                if (!TimeSpan.TryParse(child.Value, CultureInfo.InvariantCulture, out var value) || value <= TimeSpan.Zero)
                {
                    problems.Add($"{section}__{child.Key} must be a positive TimeSpan (d.hh:mm:ss), not '{child.Value}'.");
                }
            }
        }
    }
}

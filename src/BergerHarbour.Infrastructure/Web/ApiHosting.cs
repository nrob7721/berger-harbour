using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;
using BergerHarbour.Application;
using BergerHarbour.Application.Common;
using BergerHarbour.Infrastructure.Email;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;

namespace BergerHarbour.Infrastructure.Web;

/// <summary>Configuration keys (environment variables / appsettings).</summary>
public static class ConfigKeys
{
    public const string PublicBookingBaseUrl = "PUBLIC_BOOKING_BASE_URL";
    public const string CorsAllowedOrigin = "CORS_ALLOWED_ORIGIN";
    public const string EdgeProxySecret = "EDGE_PROXY_SECRET";
    public const string RateLimitPerMinute = "RATE_LIMIT_PER_MINUTE";
    public const string FirestoreProjectId = "FIRESTORE_PROJECT_ID";
    public const string PaymentLinkSecret = "PAYMENT_LINK_SECRET";
    public const string StripeSecretKey = "STRIPE_SECRET_KEY";
    public const string StripeWebhookSecret = "STRIPE_WEBHOOK_SECRET";
    public const string TurnstileSecret = "TURNSTILE_SECRET";
    public const string EmailMode = "EMAIL_MODE";
    public const string EmailSender = "EMAIL_SENDER";
    public const string EmailSenderName = "EMAIL_SENDER_NAME";
    public const string EmailSubjectPrefix = "EMAIL_SUBJECT_PREFIX";
    public const string EmailOutputDir = "EMAIL_OUTPUT_DIR";
    public const string SmtpPassword = "SMTP_PASSWORD";
    public const string CfAccessTeamDomain = "CF_ACCESS_TEAM_DOMAIN";
    public const string CfAccessAud = "CF_ACCESS_AUD";
    public const string AdminAllowedEmails = "ADMIN_ALLOWED_EMAILS";
    public const string SchedulerServiceAccountEmail = "SCHEDULER_SERVICE_ACCOUNT_EMAIL";
    public const string SchedulerAudience = "SCHEDULER_AUDIENCE";
    public const string DevAdminEmail = "DEV_ADMIN_EMAIL";
    public const string DevJobsBypass = "DEV_JOBS_BYPASS";
}

/// <summary>Startup configuration shared by both APIs.</summary>
public static class ApiHosting
{
    public const string LocalEnvironment = "Local";

    /// <summary>True while the build runs the app to write the OpenAPI document; nothing external is touched then.</summary>
    public static bool IsOpenApiGeneration => Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    public static bool IsLocal(this IHostEnvironment env) => env.IsEnvironment(LocalEnvironment);

    /// <summary>Collects every startup configuration problem so a misconfigured service refuses to start with a clear message.</summary>
    public sealed class StartupChecks(IConfiguration config, IHostEnvironment env)
    {
        private readonly List<string> _errors = [];

        public string Require(string key)
        {
            var value = config[key];
            if (string.IsNullOrWhiteSpace(value)) _errors.Add($"{key} is required.");
            return value ?? "";
        }

        /// <summary>Required everywhere except Local, where an empty value turns the check off.</summary>
        public string RequireOutsideLocal(string key) => env.IsLocal() ? config[key] ?? "" : Require(key);

        public void Add(IEnumerable<string> errors) => _errors.AddRange(errors);
        public void Add(string error) => _errors.Add(error);

        public void ForbidOutsideLocal(params string[] keys)
        {
            if (env.IsLocal()) return;
            foreach (var key in keys)
                if (!string.IsNullOrWhiteSpace(config[key])) _errors.Add($"{key} is only allowed when ASPNETCORE_ENVIRONMENT=Local.");
        }

        public void ThrowIfAny()
        {
            if (_errors.Count > 0 && !IsOpenApiGeneration)
                throw new InvalidOperationException("Invalid configuration:\n - " + string.Join("\n - ", _errors));
        }
    }

    /// <summary>Per-IP limit: RATE_LIMIT_PER_MINUTE (0 disables), default 60.</summary>
    public static int RateLimit(IConfiguration config, StartupChecks checks)
    {
        var raw = config[ConfigKeys.RateLimitPerMinute];
        if (string.IsNullOrWhiteSpace(raw)) return 60;
        if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 0) return n;
        checks.Add($"{ConfigKeys.RateLimitPerMinute} must be a whole number of 0 or more (0 disables the limit).");
        return 0;
    }

    public static void ConfigureCommon(WebApplicationBuilder builder, string documentTitle, StartupChecks checks)
    {
        var config = builder.Configuration;
        var services = builder.Services;

        if (!builder.Environment.IsLocal() && !builder.Environment.IsEnvironment("Testing"))
            builder.Logging.ClearProviders().AddJsonConsole(o => o.IncludeScopes = false);

        services.ConfigureHttpJsonOptions(o => ConfigureJson(o.SerializerOptions));
        services.AddControllers().AddJsonOptions(o => ConfigureJson(o.JsonSerializerOptions));
        services.AddProblemDetails();
        services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 256 * 1024);

        services.AddOpenApi("v1", o =>
        {
            o.AddDocumentTransformer((doc, _, _) =>
            {
                doc.Info = new OpenApiInfo { Title = documentTitle, Version = "v1" };
                doc.Servers = [];
                return Task.CompletedTask;
            });
            // Every property is always serialised (nulls included), so mark them all required for the generated TypeScript types.
            o.AddSchemaTransformer((schema, _, _) =>
            {
                if (schema.Properties is { Count: > 0 } props)
                {
                    schema.Required ??= new HashSet<string>();
                    foreach (var name in props.Keys) schema.Required.Add(name);
                }
                return Task.CompletedTask;
            });
        });

        // Strict CORS: only this API's own frontend origin, no default, no credentials. Traffic is same-origin through the proxy anyway.
        var origin = checks.Require(ConfigKeys.CorsAllowedOrigin);
        services.AddCors(o => o.AddDefaultPolicy(p =>
        {
            if (!string.IsNullOrWhiteSpace(origin)) p.WithOrigins(origin.TrimEnd('/')).AllowAnyHeader().AllowAnyMethod();
        }));

        services.AddOptions<NotificationOptions>().Bind(config.GetSection(NotificationOptions.Section));
        services.AddOptions<BookingOptions>().Bind(config.GetSection(BookingOptions.Section));
        var notificationOptions = config.GetSection(NotificationOptions.Section).Get<NotificationOptions>() ?? new NotificationOptions();
        var bookingOptions = config.GetSection(BookingOptions.Section).Get<BookingOptions>() ?? new BookingOptions();
        checks.Add(notificationOptions.Validate());
        checks.Add(bookingOptions.Validate());

        var baseUrl = checks.Require(ConfigKeys.PublicBookingBaseUrl);
        services.Configure<AppUrlOptions>(o => o.PublicBookingBaseUrl = baseUrl);
        checks.ForbidOutsideLocal(ConfigKeys.DevAdminEmail, ConfigKeys.DevJobsBypass);

        services.AddApplicationCore();
        services.AddInfrastructure(config, builder.Environment, checks);
    }

    public static void ConfigureJson(System.Text.Json.JsonSerializerOptions o)
    {
        o.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        o.Converters.Add(new JsonStringEnumConverter());
        o.NumberHandling = JsonNumberHandling.Strict;
        o.RespectNullableAnnotations = true;
        o.RespectRequiredConstructorParameters = true;
    }

    /// <summary>Middleware shared by both APIs, in order: errors, CORS, proxy secret (then whatever the API adds).</summary>
    public static void UseCommonPipeline(WebApplication app, string? proxySecret, IReadOnlyList<string> proxySecretExemptPaths)
    {
        app.UseExceptionHandler();
        app.UseCors();
        app.UseMiddleware<EdgeProxySecretMiddleware>(proxySecret ?? "", proxySecretExemptPaths);
    }

    public static void MapCommonEndpoints(WebApplication app)
    {
        if (app.Environment.IsLocal()) app.MapOpenApi();
        app.MapControllers();
    }

    public static EmailMode ParseEmailMode(IConfiguration config) =>
        Enum.TryParse<EmailMode>(config[ConfigKeys.EmailMode], true, out var m) ? m : EmailMode.Smtp;

    public static IResult Healthy() => Results.Ok(new { status = "ok" });
}

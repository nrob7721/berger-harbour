using System.Text.Json.Serialization;
using BergerHarbour.Infrastructure.Cloudflare;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BergerHarbour.Infrastructure.Web;

/// <summary>Hosting conventions shared by public-api and admin-api.</summary>
public static class WebDefaults
{
    public const string CorsPolicy = "frontend";

    public static WebApplicationBuilder AddBergerHarbourWeb(this WebApplicationBuilder builder, long maxRequestBodyBytes,
        string defaultCorsOrigin)
    {
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = maxRequestBodyBytes);
        var port = Environment.GetEnvironmentVariable("PORT");
        if (!string.IsNullOrEmpty(port))
        {
            builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        }

        if (!builder.Environment.IsDevelopment())
        {
            // Structured JSON logs for Cloud Logging. Never log card data or full tokens.
            builder.Logging.ClearProviders();
            builder.Logging.AddJsonConsole(o =>
            {
                o.IncludeScopes = false;
                o.UseUtcTimestamp = true;
                o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
            });
        }

        builder.Services.AddControllers().AddJsonOptions(o =>
        {
            o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
            o.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ApiExceptionHandler>();
        builder.Services.AddOpenApi();

        var origin = builder.Configuration["CORS_ALLOWED_ORIGIN"] ?? defaultCorsOrigin;
        builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p => p
            .WithOrigins(origin.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .WithMethods("GET", "POST", "PUT", "DELETE")
            .WithHeaders("Content-Type")
            .DisallowCredentials()));
        return builder;
    }

    public static WebApplication UseBergerHarbourWeb(this WebApplication app, params string[] proxySecretExemptPrefixes)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseCors(CorsPolicy);
        app.UseMiddleware<EdgeProxySecretMiddleware>(
            app.Configuration["EDGE_PROXY_SECRET"] ?? string.Empty,
            app.Environment.IsDevelopment(),
            (IReadOnlyList<PathString>)proxySecretExemptPrefixes.Select(p => new PathString(p)).ToList());
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        return app;
    }

    /// <summary>The visitor's IP as forwarded by the Pages Function (CF-Connecting-IP).</summary>
    public static string? ClientIp(this HttpContext context) =>
        context.Request.Headers["CF-Connecting-IP"].FirstOrDefault() ?? context.Connection.RemoteIpAddress?.ToString();

    public static void AddCloudflareAccessGuard(this WebApplication app, params string[] exemptPrefixes) =>
        app.UseMiddleware<CloudflareAccessMiddleware>(
            (IReadOnlyList<PathString>)exemptPrefixes.Select(p => new PathString(p)).ToList());
}

/// <summary>Admin-api: every route except the exempt ones needs a valid Cloudflare Access JWT for an allowed email.</summary>
public sealed class CloudflareAccessMiddleware(RequestDelegate next, IReadOnlyList<PathString> exemptPrefixes,
    CloudflareAccessValidator validator, Microsoft.Extensions.Options.IOptions<CloudflareAccessOptions> options)
{
    public const string HeaderName = "Cf-Access-Jwt-Assertion";
    public const string EmailItem = "AdminEmail";

    public async Task InvokeAsync(HttpContext context)
    {
        if (exemptPrefixes.Any(p => context.Request.Path.StartsWithSegments(p)) || HttpMethods.IsOptions(context.Request.Method) ||
            !context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        if (options.Value.DevBypassEmail is { Length: > 0 } devEmail)
        {
            context.Items[EmailItem] = devEmail;
            await next(context);
            return;
        }

        var result = await validator.ValidateAsync(context.Request.Headers[HeaderName].FirstOrDefault(), context.RequestAborted);
        if (!result.IsValid)
        {
            context.Response.StatusCode = result.Email is null ? StatusCodes.Status401Unauthorized : StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { title = result.Error });
            return;
        }

        context.Items[EmailItem] = result.Email;
        await next(context);
    }
}

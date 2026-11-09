using System.Text.Json;
using System.Text.Json.Serialization;
using BergerHarbour.Application;
using BergerHarbour.Infrastructure.Cloudflare;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BergerHarbour.Infrastructure.Web;

/// <summary>Hosting defaults shared by both APIs: JSON, ProblemDetails, OpenAPI, strict CORS, request limits and middleware.</summary>
public static class BergerHosting
{
    public static WebApplicationBuilder AddBergerApi(this WebApplicationBuilder builder, ApiKind api)
    {
        var configuration = builder.Configuration;
        var environment = builder.Environment;
        if (!StartupConfiguration.IsGeneratingOpenApiDocument)
        {
            StartupConfiguration.Validate(configuration, environment, api);
        }

        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = api == ApiKind.Public ? 256 * 1024 : 1024 * 1024);
        if (!StartupConfiguration.IsLocal(environment))
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddJsonConsole(o => o.UseUtcTimestamp = true);
        }

        builder.Services
            .AddControllers(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
            .AddJsonOptions(o => Configure(o.JsonSerializerOptions))
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = context =>
            {
                var problem = new ValidationProblemDetails(context.ModelState)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Some of the values sent aren't valid.",
                };
                return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
            });
        builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o => Configure(o.SerializerOptions));
        builder.Services.AddOpenApi(o => o.AddSchemaTransformer<RequiredPropertiesSchemaTransformer>());
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ApiExceptionHandler>();
        builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
        {
            var origin = configuration[ConfigKeys.CorsAllowedOrigin];
            if (!string.IsNullOrWhiteSpace(origin))
            {
                p.WithOrigins(origin.TrimEnd('/')).WithMethods("GET", "POST", "PUT", "DELETE").AllowAnyHeader();
            }
        }));
        builder.Services.AddBergerApplication();
        builder.Services.AddBergerInfrastructure(configuration, environment);
        return builder;
    }

    public static WebApplication UseBergerApi(this WebApplication app, ApiKind api)
    {
        var configuration = app.Configuration;
        var local = StartupConfiguration.IsLocal(app.Environment);
        app.UseExceptionHandler();
        app.UseCors();
        app.UseMiddleware<EdgeProxySecretMiddleware>(configuration[ConfigKeys.EdgeProxySecret] ?? string.Empty);
        if (api == ApiKind.Public)
        {
            app.UseMiddleware<RateLimitingMiddleware>(StartupConfiguration.IsGeneratingOpenApiDocument
                ? 0
                : StartupConfiguration.RateLimitPerMinute(configuration, app.Environment));
        }
        else
        {
            app.UseMiddleware<CloudflareAccessMiddleware>(local ? configuration[ConfigKeys.DevAdminEmail] ?? string.Empty : string.Empty);
            app.UseMiddleware<SchedulerOidcMiddleware>(local && StartupConfiguration.IsTrue(configuration[ConfigKeys.DevJobsBypass]));
        }

        app.MapControllers();
        return app;
    }

    private static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.NumberHandling = JsonNumberHandling.Strict;
        options.Converters.Add(new JsonStringEnumConverter());
    }
}

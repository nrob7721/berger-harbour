using BergerHarbour.Application;
using BergerHarbour.Application.Abstractions;
using BergerHarbour.Infrastructure.Stripe;
using BergerHarbour.Infrastructure.Turnstile;
using BergerHarbour.Infrastructure.Web;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
var local = builder.Environment.IsLocal();
var checks = new ApiHosting.StartupChecks(config, builder.Environment);

ApiHosting.ConfigureCommon(builder, "Berger Houseboats public API", checks);
var proxySecret = checks.RequireOutsideLocal(ConfigKeys.EdgeProxySecret);
var rateLimit = ApiHosting.RateLimit(config, checks);

var stripeKey = checks.RequireOutsideLocal(ConfigKeys.StripeSecretKey);
var stripeWebhookSecret = checks.RequireOutsideLocal(ConfigKeys.StripeWebhookSecret);
builder.Services.Configure<StripeOptions>(o =>
{
    o.SecretKey = stripeKey;
    o.WebhookSecret = stripeWebhookSecret;
});
var turnstileSecret = local && string.IsNullOrWhiteSpace(config[ConfigKeys.TurnstileSecret])
    ? TurnstileOptions.TestSecret
    : checks.Require(ConfigKeys.TurnstileSecret);
builder.Services.Configure<TurnstileOptions>(o => o.Secret = turnstileSecret);
builder.Services.AddHttpClient<ITurnstileVerifier, TurnstileVerifier>(c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<IPaymentGateway, StripePaymentGateway>();
builder.Services.AddSingleton<StripeWebhookParser>();
builder.Services.AddPublicUseCases();

checks.ThrowIfAny();

var app = builder.Build();
string[] webhook = ["/api/stripe/webhook"];
ApiHosting.UseCommonPipeline(app, proxySecret, webhook);
// Straight after the proxy-secret check and before any controller or Firestore access.
app.UseMiddleware<RateLimitingMiddleware>(rateLimit, webhook);
ApiHosting.MapCommonEndpoints(app);
app.Run();

/// <summary>Entry point, visible to the integration tests' WebApplicationFactory.</summary>
public partial class Program;

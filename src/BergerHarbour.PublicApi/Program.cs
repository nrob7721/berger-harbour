using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Infrastructure.Web;

var builder = WebApplication.CreateBuilder(args);
builder.AddBergerApi(ApiKind.Public);
builder.Services.AddSingleton<IBookingRequestValidationStrategy, OnlineBookingValidationStrategy>();

var app = builder.Build();
app.UseBergerApi(ApiKind.Public);
app.Run();

/// <summary>The public API entry point (customers and the Stripe webhook).</summary>
public partial class Program;

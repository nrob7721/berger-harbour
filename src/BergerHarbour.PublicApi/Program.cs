using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Infrastructure;
using BergerHarbour.Infrastructure.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddBergerHarbourWeb(maxRequestBodyBytes: 64 * 1024, defaultCorsOrigin: "https://book.bergerhouseboats.com.au");
builder.Services.AddBergerHarbourInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());

// Customer bookings use the online rules (period shape, lead time, open-until, blocked periods, …).
builder.Services.AddSingleton<IBookingRequestValidationStrategy, OnlineBookingValidationStrategy>();

var app = builder.Build();
app.UseBergerHarbourWeb(BergerHarbour.PublicApi.Controllers.StripeWebhookController.Route);
app.MapControllers();
app.Run();

public partial class Program;

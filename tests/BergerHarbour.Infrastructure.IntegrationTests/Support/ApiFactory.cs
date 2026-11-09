using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BergerHarbour.Infrastructure.IntegrationTests.Support;

/// <summary>An API host for tests; <typeparamref name="TEntryPoint"/> is any public type of that API's assembly.</summary>
public sealed class ApiFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    private readonly string environment;
    private readonly IReadOnlyDictionary<string, string?> settings;

    public ApiFactory(string environment, IReadOnlyDictionary<string, string?> settings)
    {
        this.environment = environment;
        this.settings = settings;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }
}

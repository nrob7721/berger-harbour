using Microsoft.Extensions.Hosting;

namespace BergerHarbour.Infrastructure;

/// <summary>
/// Environments: <c>Local</c> (a developer machine, against emulators), <c>Development</c> (the deployed dev project) and
/// <c>Production</c>. Only Local relaxes security checks; both deployed environments behave the same.
/// </summary>
public static class HostEnvironmentExtensions
{
    public const string Local = "Local";

    public static bool IsLocal(this IHostEnvironment environment) => environment.IsEnvironment(Local);
}

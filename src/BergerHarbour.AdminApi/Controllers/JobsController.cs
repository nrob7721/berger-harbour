using BergerHarbour.Application.Notifications;
using BergerHarbour.Infrastructure.Google;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BergerHarbour.AdminApi.Controllers;

/// <summary>
/// Called hourly by Cloud Scheduler. Not behind Cloudflare Access or the proxy secret: it validates the Google OIDC
/// token instead (issuer Google, audience = service URL, email = the Scheduler service account).
/// </summary>
[ApiController]
[Route(Route)]
public sealed class JobsController(NotificationJob job, SchedulerOidcValidator oidc, IOptions<SchedulerOidcOptions> options,
    ILogger<JobsController> logger) : ControllerBase
{
    public const string Route = "/api/jobs";

    [HttpPost("process-notifications")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> ProcessNotifications(CancellationToken ct)
    {
        var header = Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : null;
        if (!options.Value.DevBypass && !await oidc.ValidateAsync(token))
        {
            return Unauthorized();
        }

        var result = await job.RunAsync(ct);
        if (result.Failures > 0)
        {
            logger.LogError("JOB_FAILURE: process-notifications had {Failures} failures", result.Failures);
            return StatusCode(StatusCodes.Status500InternalServerError, result);
        }

        return Ok(result);
    }
}

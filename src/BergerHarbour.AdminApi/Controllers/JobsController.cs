using BergerHarbour.Application.Notifications;
using BergerHarbour.Application.Notifications.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.AdminApi.Controllers;

/// <summary>Cloud Scheduler's hourly call. Not behind Access: the OIDC middleware authenticates it.</summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/jobs")]
public sealed class JobsController : ControllerBase
{
    private readonly NotificationJobService service;

    public JobsController(NotificationJobService service)
    {
        this.service = service;
    }

    [HttpPost("process-notifications")]
    public Task<NotificationJobResult> ProcessNotifications(CancellationToken ct) => service.RunAsync(ct);
}

using BergerHarbour.Application.Settings;
using BergerHarbour.Application.Settings.Dtos;
using BergerHarbour.Domain.Settings;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.AdminApi.Controllers;

[ApiController]
[Route("api/settings/email-templates")]
public sealed class EmailTemplatesController : ControllerBase
{
    private readonly EmailTemplateService service;

    public EmailTemplatesController(EmailTemplateService service)
    {
        this.service = service;
    }

    [HttpGet]
    public Task<EmailTemplatesResponse> List(CancellationToken ct) => service.ListAsync(ct);

    [HttpPut("{key}")]
    public Task<EmailTemplateDto> Update(EmailTemplateKey key, SaveEmailTemplateRequest request, CancellationToken ct) =>
        service.UpdateAsync(key, request, ct);

    /// <summary>Sends a sample to the staff alert address.</summary>
    [HttpPost("{key}/test")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SendTest(EmailTemplateKey key, CancellationToken ct)
    {
        await service.SendTestAsync(key, ct);
        return NoContent();
    }
}

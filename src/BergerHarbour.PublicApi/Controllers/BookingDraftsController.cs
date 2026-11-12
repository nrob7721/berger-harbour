using BergerHarbour.Application.BookingDrafts;
using BergerHarbour.Application.BookingDrafts.Dtos;
using BergerHarbour.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.PublicApi.Controllers;

[ApiController]
[Route("api/booking-drafts")]
public sealed class BookingDraftsController : ControllerBase
{
    private readonly BookingDraftService service;

    public BookingDraftsController(BookingDraftService service)
    {
        this.service = service;
    }

    [HttpPost]
    public Task<CreateDraftResponse> Create(CreateDraftRequest request, CancellationToken ct) =>
        service.CreateAsync(request, ApiPaths.ClientIp(HttpContext), ct);

    [HttpGet("{draftId}")]
    public Task<DraftDetailsResponse> Get(string draftId, CancellationToken ct) =>
        service.GetDetailsAsync(draftId, ct);

    [HttpPut("{draftId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(string draftId, UpdateDraftRequest request, CancellationToken ct)
    {
        await service.UpdateAsync(draftId, request, ct);
        return NoContent();
    }
}

using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.BookingDrafts.Dtos;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.BookingDrafts;

/// <summary>Booking drafts (<c>BookingDraftsController</c>). A draft is never a customer record.</summary>
public sealed class BookingDraftService
{
    public const string TurnstileFailed = "Please complete the security check and try again.";

    private readonly IBookingDraftRepository drafts;
    private readonly IBoatRepository boats;
    private readonly ITurnstileVerifier turnstile;
    private readonly StayPeriodFactory periods;
    private readonly IClock clock;

    public BookingDraftService(IBookingDraftRepository drafts, IBoatRepository boats, ITurnstileVerifier turnstile, StayPeriodFactory periods, IClock clock)
    {
        this.drafts = drafts;
        this.boats = boats;
        this.turnstile = turnstile;
        this.periods = periods;
        this.clock = clock;
    }

    /// <summary>Verifies Turnstile, validates the details and creates the draft.</summary>
    public async Task<CreateDraftResponse> CreateAsync(CreateDraftRequest request, string? remoteIp, CancellationToken ct = default)
    {
        if (!await turnstile.VerifyAsync(request.TurnstileToken, remoteIp, ct))
        {
            throw new DomainValidationException("turnstileToken", TurnstileFailed);
        }

        var contents = await ContentsAsync(request.Slug, request.PeriodType, request.StartDate, request.NumberOfAdults, request.NumberOfChildren,
            request.FirstName, request.LastName, request.Email, request.Mobile, [], DraftStep.Details, ct);
        var draft = BookingDraft.Create(BookingDraftId.New(), contents, clock.UtcNow);
        await drafts.SaveAsync(draft, ct);
        return new CreateDraftResponse(draft.Id.Value);
    }

    /// <summary>Updates the draft (including its boat) and its last activity. 404 if unknown, converted or deleted.</summary>
    public async Task UpdateAsync(string draftId, UpdateDraftRequest request, CancellationToken ct = default)
    {
        var draft = await drafts.GetAsync(new BookingDraftId(draftId), ct);
        if (draft is null || draft.IsConverted)
        {
            throw new NotFoundException("Your booking session has expired. Please start again.");
        }

        var addons = (request.Addons ?? []).Where(a => !string.IsNullOrWhiteSpace(a.AddonId))
            .Select(a => new AddonRequest(new AddonId(a.AddonId), a.Quantity)).ToList();
        var contents = await ContentsAsync(request.Slug, request.PeriodType, request.StartDate, request.NumberOfAdults, request.NumberOfChildren,
            request.FirstName, request.LastName, request.Email, request.Mobile, addons, request.LastStepReached, ct);
        draft.Update(contents, clock.UtcNow);
        await drafts.SaveAsync(draft, ct);
    }

    private async Task<DraftContents> ContentsAsync(
        string slug, PeriodType periodType, DateOnly startDate, int adults, int children, string? firstName, string? lastName, string? email,
        string? mobile, IReadOnlyList<AddonRequest> addons, DraftStep step, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        var boat = await boats.GetBySlugAsync(slug ?? string.Empty, ct);
        if (boat is null || !boat.IsActive || boat.Type != BoatType.House)
        {
            errors.Add("slug", "This boat isn't available for online booking.");
        }

        var stay = errors.Capture(() => (StayRange?)periods.ForOnlineStart(periodType, startDate));
        if (string.IsNullOrWhiteSpace(firstName))
        {
            errors.Add("firstName", "First name is required.");
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            errors.Add("lastName", "Last name is required.");
        }

        var emailAddress = errors.Capture(() => EmailAddress.Create(email));
        var mobileNumber = errors.Capture(() => MobileNumber.Create(mobile));
        errors.ThrowIfAny();
        return new DraftContents(boat!.Id, periodType, stay!.Value, adults, children, firstName!.Trim(), lastName!.Trim(), emailAddress!, mobileNumber!,
            addons, step);
    }
}

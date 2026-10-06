using BergerHarbour.Application.Common;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Infrastructure.Stripe;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BergerHarbour.Infrastructure.Web;

/// <summary>
/// Maps exceptions to ProblemDetails: validation → 400 with per-field errors, overlaps and version conflicts → 409,
/// missing records → 404, anything else → 500.
/// </summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem = exception switch
        {
            RequestValidationException ex => new ValidationProblemDetails(ex.Errors.ToDictionary(e => e.Key, e => e.Value))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Please check the highlighted fields.",
            },
            DomainValidationException ex => new ValidationProblemDetails(new Dictionary<string, string[]> { [ex.Field] = [ex.Message] })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = ex.Message,
            },
            AvailabilityConflictException ex => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = ex.Message,
                Type = "availability-conflict",
                Extensions = { ["conflictingReferences"] = ex.ConflictingReferences },
            },
            ConcurrencyConflictException ex => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = ex.Message,
                Type = "concurrency-conflict",
            },
            NotFoundException ex => new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = ex.Message },
            InvalidStripeSignatureException ex => new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = ex.Message },
            BadHttpRequestException ex => new ProblemDetails { Status = ex.StatusCode, Title = "Bad request." },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Something went wrong. Please try again.",
            },
        };

        if (problem.Status >= 500)
        {
            logger.LogError(exception, "Unhandled error on {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        else
        {
            logger.LogInformation("{Status} on {Method} {Path}: {Message}", problem.Status, context.Request.Method,
                context.Request.Path, exception.Message);
        }

        context.Response.StatusCode = problem.Status ?? 500;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}

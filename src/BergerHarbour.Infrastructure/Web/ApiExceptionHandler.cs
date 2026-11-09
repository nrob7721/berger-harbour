using BergerHarbour.Application.Abstractions;
using BergerHarbour.Domain.Shared;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BergerHarbour.Infrastructure.Web;

/// <summary>
/// Maps exceptions to ProblemDetails: field errors → 400 with <c>errors</c>, rule breaks → 400, not found → 404, overlaps
/// and version conflicts → 409 (with <c>conflictingReferences</c>), anything else → 500.
/// </summary>
public sealed class ApiExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ApiExceptionHandler> logger;

    public ApiExceptionHandler(ILogger<ApiExceptionHandler> logger)
    {
        this.logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem = exception switch
        {
            DomainValidationException e => new ValidationProblemDetails(e.Errors.ByField.ToDictionary(kv => kv.Key, kv => kv.Value))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = e.Message,
            },
            BusinessRuleException e => new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = e.Message },
            InvalidWebhookException e => new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = e.Message },
            NotFoundException e => new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = e.Message },
            ConflictException e => Conflict(e.Message, e.ConflictingReferences),
            ConcurrencyException e => Conflict(e.Message, []),
            BadHttpRequestException e => new ProblemDetails { Status = e.StatusCode, Title = "The request couldn't be read." },
            _ => new ProblemDetails { Status = StatusCodes.Status500InternalServerError, Title = "Something went wrong. Please try again." },
        };

        if (problem.Status >= 500)
        {
            logger.LogError(exception, "Unhandled error on {Method} {Path}", context.Request.Method, context.Request.Path.Value);
        }

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(problem, problem.GetType(), (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", ct);
        return true;
    }

    private static ProblemDetails Conflict(string message, IReadOnlyList<string> references)
    {
        var problem = new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = message };
        problem.Extensions["conflictingReferences"] = references;
        return problem;
    }
}

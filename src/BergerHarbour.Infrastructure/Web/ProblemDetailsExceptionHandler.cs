using BergerHarbour.Application.Common;
using BergerHarbour.Domain.Shared;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BergerHarbour.Infrastructure.Web;

/// <summary>Maps domain and application exceptions to ProblemDetails: 400 with per-field errors, 404, 409 and 502.</summary>
public sealed class ProblemDetailsExceptionHandler(ILogger<ProblemDetailsExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem = exception switch
        {
            DomainValidationException v => new ValidationProblemDetails(v.Errors.ToDictionary(kv => kv.Key, kv => kv.Value))
            {
                Status = 400, Title = v.Message,
            },
            ConflictException c => WithReferences(new ProblemDetails { Status = 409, Title = c.Message }, c.ConflictingReferences),
            ConcurrencyConflictException cc => new ProblemDetails { Status = 409, Title = cc.Message },
            NotFoundException n => new ProblemDetails { Status = 404, Title = n.Message },
            ExternalServiceException x => new ProblemDetails { Status = 502, Title = x.Message },
            BadHttpRequestException b => new ProblemDetails { Status = b.StatusCode, Title = "The request couldn't be read." },
            OperationCanceledException when ct.IsCancellationRequested => new ProblemDetails { Status = 499, Title = "Request cancelled." },
            _ => new ProblemDetails { Status = 500, Title = "Something went wrong. Please try again." },
        };
        if (problem.Status >= 500 && problem.Status != 502)
            logger.LogError(exception, "Unhandled error on {Method} {Path}", context.Request.Method, context.Request.Path);
        else if (problem.Status == 502)
            logger.LogError(exception, "External service failed on {Path}", context.Request.Path);
        context.Response.StatusCode = problem.Status ?? 500;
        await context.Response.WriteAsJsonAsync(problem, problem.GetType(), (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", ct);
        return true;
    }

    private static ProblemDetails WithReferences(ProblemDetails p, IReadOnlyList<string> references)
    {
        if (references.Count > 0) p.Extensions["conflictingReferences"] = references;
        return p;
    }
}

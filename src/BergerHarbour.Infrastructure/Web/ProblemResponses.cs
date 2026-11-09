using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.Infrastructure.Web;

/// <summary>Writes a ProblemDetails response from middleware.</summary>
public static class ProblemResponses
{
    public static async Task WriteAsync(HttpContext context, int status, string title)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = status, Title = title }, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json",
            context.RequestAborted);
    }
}

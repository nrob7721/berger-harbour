using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace BergerHarbour.Infrastructure.Web;

/// <summary>
/// Every DTO property is always serialised (null when empty), so the generated TypeScript types mark them all as
/// present; nullability stays in the type (<c>null | string</c>).
/// </summary>
public sealed class RequiredPropertiesSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (schema.Properties is { Count: > 0 } properties)
        {
            schema.Required = new HashSet<string>(properties.Keys);
        }

        return Task.CompletedTask;
    }
}

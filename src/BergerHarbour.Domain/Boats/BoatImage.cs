using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Boats;

/// <summary>
/// One of a boat's photos (§7.3): an absolute https:// URL of an image on the WordPress site (<c>bergerhouseboats.com.au</c> or
/// <c>www.bergerhouseboats.com.au</c>), with an optional caption of at most 120 characters. Stored trimmed; a blank caption is none.
/// </summary>
public sealed record BoatImage
{
    public const int MaxDescriptionLength = 120;
    public const string UrlError = "Use an https:// link to an image on bergerhouseboats.com.au.";
    public const string DescriptionError = "Keep the description to 120 characters.";

    private BoatImage(string url, string? description)
    {
        Url = url;
        Description = description;
    }

    public string Url { get; }

    public string? Description { get; }

    /// <summary>
    /// Validates both parts; errors are on <c>{field}.url</c> and <c>{field}.description</c>, e.g. <c>images[2].url</c>.
    /// </summary>
    public static BoatImage Create(string? url, string? description, string field = "image")
    {
        var errors = new ValidationErrors();
        var trimmedUrl = (url ?? string.Empty).Trim();
        if (!IsValidUrl(trimmedUrl))
        {
            errors.Add($"{field}.url", UrlError);
        }

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription?.Length > MaxDescriptionLength)
        {
            errors.Add($"{field}.description", DescriptionError);
        }

        errors.ThrowIfAny();
        return new BoatImage(trimmedUrl, trimmedDescription);
    }

    /// <summary>An absolute https:// URL on bergerhouseboats.com.au or www.bergerhouseboats.com.au.</summary>
    public static bool IsValidUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.ToLowerInvariant() is "bergerhouseboats.com.au" or "www.bergerhouseboats.com.au";
}

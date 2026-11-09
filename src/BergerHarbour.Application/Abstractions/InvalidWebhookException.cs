namespace BergerHarbour.Application.Abstractions;

/// <summary>The webhook's signature or payload is invalid (400).</summary>
public sealed class InvalidWebhookException : Exception
{
    public InvalidWebhookException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

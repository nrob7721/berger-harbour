namespace BergerHarbour.Domain.Shared;

/// <summary>The requested record does not exist (404).</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string message)
        : base(message)
    {
    }
}

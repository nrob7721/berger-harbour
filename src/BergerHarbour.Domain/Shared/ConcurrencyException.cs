namespace BergerHarbour.Domain.Shared;

/// <summary>An optimistic concurrency conflict: the record was changed since it was read (409).</summary>
public sealed class ConcurrencyException : Exception
{
    public const string DefaultMessage = "This record was changed elsewhere — reload and retry.";

    public ConcurrencyException()
        : base(DefaultMessage)
    {
    }
}

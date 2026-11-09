namespace BergerHarbour.Domain.Shared;

/// <summary>A rule conflict such as overlapping dates (409). Optionally names the conflicting booking references.</summary>
public sealed class ConflictException : Exception
{
    public const string DatesTaken = "Those dates are no longer available.";

    public ConflictException(string message, IReadOnlyList<string>? conflictingReferences = null)
        : base(message)
    {
        ConflictingReferences = conflictingReferences ?? [];
    }

    public IReadOnlyList<string> ConflictingReferences { get; }
}

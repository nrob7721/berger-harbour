namespace BergerHarbour.Domain.Shared;

/// <summary>A rule was broken. <see cref="Field"/> names the offending input, when there is one.</summary>
public class DomainValidationException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

/// <summary>The requested change would make two occupancies of the same boat overlap.</summary>
public class AvailabilityConflictException(string message, IReadOnlyList<string> conflictingReferences) : Exception(message)
{
    public const string DatesNoLongerAvailable = "Those dates are no longer available";

    public IReadOnlyList<string> ConflictingReferences { get; } = conflictingReferences;
}

/// <summary>The record was changed by someone else since it was loaded.</summary>
public class ConcurrencyConflictException() : Exception(DefaultMessage)
{
    public const string DefaultMessage = "This record was changed elsewhere — reload and retry.";
}

/// <summary>A state transition that the aggregate does not allow.</summary>
public class InvalidStateTransitionException(string message) : DomainValidationException("status", message);

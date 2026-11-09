namespace BergerHarbour.Domain.Shared;

/// <summary>Invalid input, reported as a 400 ProblemDetails with per-field errors.</summary>
public sealed class DomainValidationException : Exception
{
    public DomainValidationException(ValidationErrors errors)
        : base(errors.Messages.FirstOrDefault() ?? "The request is invalid.")
    {
        Errors = errors;
    }

    public DomainValidationException(string field, string message)
        : this(new ValidationErrors().Add(field, message))
    {
    }

    public ValidationErrors Errors { get; }
}

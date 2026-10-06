namespace BergerHarbour.Application.Common;

public sealed class NotFoundException(string message) : Exception(message);

/// <summary>One or more input fields are invalid. Maps to 400 with per-field errors.</summary>
public sealed class RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public static RequestValidationException For(string field, string message) => new(new Dictionary<string, string[]>
    {
        [field] = [message],
    });
}

/// <summary>Collects per-field validation errors.</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool IsValid => _errors.Count == 0;

    public IReadOnlyDictionary<string, string[]> ToDictionary() => _errors.ToDictionary(e => e.Key, e => e.Value.ToArray());

    public ValidationErrors Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            _errors[field] = list = [];
        }

        list.Add(message);
        return this;
    }

    public bool Has(string field) => _errors.ContainsKey(field);

    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new RequestValidationException(ToDictionary());
        }
    }
}

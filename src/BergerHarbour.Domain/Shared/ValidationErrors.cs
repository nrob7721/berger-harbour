namespace BergerHarbour.Domain.Shared;

/// <summary>Per-field validation errors, keyed by the camelCase API field name.</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);

    public static ValidationErrors None => new();

    public bool IsEmpty => errors.Count == 0;

    public IReadOnlyDictionary<string, string[]> ByField =>
        errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal);

    public IEnumerable<string> Messages => errors.Values.SelectMany(m => m);

    public bool Has(string field) => errors.ContainsKey(field);

    public ValidationErrors Add(string field, string message)
    {
        if (!errors.TryGetValue(field, out var list))
        {
            list = [];
            errors[field] = list;
        }

        if (!list.Contains(message))
        {
            list.Add(message);
        }

        return this;
    }

    public ValidationErrors AddRange(ValidationErrors other)
    {
        foreach (var (field, messages) in other.errors)
        {
            foreach (var message in messages)
            {
                Add(field, message);
            }
        }

        return this;
    }

    /// <summary>Runs <paramref name="action"/> and records its field error instead of throwing.</summary>
    public T? Capture<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (DomainValidationException e)
        {
            AddRange(e.Errors);
            return default;
        }
    }

    public void ThrowIfAny()
    {
        if (!IsEmpty)
        {
            throw new DomainValidationException(this);
        }
    }
}

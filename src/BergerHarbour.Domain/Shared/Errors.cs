namespace BergerHarbour.Domain.Shared;

/// <summary>A request broke a business rule. Maps to 400 with per-field errors.</summary>
public class DomainValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public DomainValidationException(string field, string message) : base(message)
    {
        Errors = new Dictionary<string, string[]> { [field] = [message] };
    }

    public DomainValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base(errors.Values.SelectMany(v => v).FirstOrDefault() ?? "The request is invalid.")
    {
        Errors = errors;
    }
}

/// <summary>Collects field errors and throws them together.</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new();

    public bool Any => _errors.Count > 0;

    public ValidationErrors Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list)) _errors[field] = list = [];
        if (!list.Contains(message)) list.Add(message);
        return this;
    }

    public void AddIf(bool condition, string field, string message)
    {
        if (condition) Add(field, message);
    }

    /// <summary>Runs an action that may throw a validation exception and records its errors instead.</summary>
    public T? Capture<T>(Func<T> action)
    {
        try { return action(); }
        catch (DomainValidationException e)
        {
            foreach (var (field, messages) in e.Errors)
                foreach (var m in messages) Add(field, m);
            return default;
        }
    }

    public async Task<T?> CaptureAsync<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (DomainValidationException e)
        {
            Merge(e.Errors);
            return default;
        }
    }

    public void Merge(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (var (field, messages) in errors)
            foreach (var m in messages) Add(field, m);
    }

    public IReadOnlyDictionary<string, string[]> ToDictionary() =>
        _errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());

    public void ThrowIfAny()
    {
        if (Any) throw new DomainValidationException(ToDictionary());
    }
}

/// <summary>The requested change clashes with existing data (overlaps, duplicates, illegal state). Maps to 409.</summary>
public sealed class ConflictException(string message, IReadOnlyList<string>? conflictingReferences = null) : Exception(message)
{
    public IReadOnlyList<string> ConflictingReferences { get; } = conflictingReferences ?? [];
}

/// <summary>Maps to 404.</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>Optimistic concurrency failure. Maps to 409.</summary>
public sealed class ConcurrencyConflictException() : Exception(Text)
{
    public const string Text = "This record was changed elsewhere — reload and retry.";
}

namespace BergerHarbour.Domain.Shared;

internal static class Guard
{
    public static string Required(string? value, string field, string label, int maxLength = 200)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainValidationException(field, $"{label} is required.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new DomainValidationException(field, $"{label} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    public static string? Optional(string? value, string field, string label, int maxLength = 2000)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > maxLength)
        {
            throw new DomainValidationException(field, $"{label} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    public static decimal Money(decimal amount, string field, string label)
    {
        if (amount < 0)
        {
            throw new DomainValidationException(field, $"{label} cannot be negative.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new DomainValidationException(field, $"{label} must be in whole cents.");
        }

        return amount;
    }

    public static decimal PositiveMoney(decimal amount, string field, string label)
    {
        Money(amount, field, label);
        if (amount == 0)
        {
            throw new DomainValidationException(field, $"{label} must be greater than zero.");
        }

        return amount;
    }
}

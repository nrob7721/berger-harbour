namespace BergerHarbour.Domain.Shared;

/// <summary>A request that breaks a rule not tied to one field, e.g. "Nothing is owing" (400).</summary>
public sealed class BusinessRuleException : Exception
{
    public BusinessRuleException(string message)
        : base(message)
    {
    }
}

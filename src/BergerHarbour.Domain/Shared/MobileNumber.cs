using PhoneNumbers;

namespace BergerHarbour.Domain.Shared;

/// <summary>
/// A customer's mobile number, stored in E.164. Valid for its country and of type MOBILE, or FIXED_LINE_OR_MOBILE where
/// the two can't be told apart (e.g. the US). Landlines are rejected.
/// </summary>
public sealed record MobileNumber
{
    public const string Error = "Enter a valid mobile number for the selected country.";

    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    private MobileNumber(string value)
    {
        Value = value;
    }

    /// <summary>E.164, e.g. +61412345678.</summary>
    public string Value { get; }

    /// <summary>National format for Australian numbers, international for others.</summary>
    public string Display
    {
        get
        {
            var number = Util.Parse(Value, null);
            return Util.Format(number, number.CountryCode == 61 ? PhoneNumberFormat.NATIONAL : PhoneNumberFormat.INTERNATIONAL);
        }
    }

    /// <summary>Parses E.164 (as the apps send it) or, without a leading +, an Australian national number.</summary>
    public static MobileNumber Create(string? raw, string field = "mobile", string requiredMessage = "Mobile is required.")
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            throw new DomainValidationException(field, requiredMessage);
        }

        PhoneNumber number;
        try
        {
            number = Util.Parse(text, "AU");
        }
        catch (NumberParseException)
        {
            throw new DomainValidationException(field, Error);
        }

        var type = Util.GetNumberType(number);
        if (!Util.IsValidNumber(number) || type is not (PhoneNumberType.MOBILE or PhoneNumberType.FIXED_LINE_OR_MOBILE))
        {
            throw new DomainValidationException(field, Error);
        }

        return new MobileNumber(Util.Format(number, PhoneNumberFormat.E164));
    }

    /// <summary>An optional mobile: blank gives null.</summary>
    public static MobileNumber? CreateOptional(string? raw, string field) =>
        string.IsNullOrWhiteSpace(raw) ? null : Create(raw, field);

    public override string ToString() => Value;
}

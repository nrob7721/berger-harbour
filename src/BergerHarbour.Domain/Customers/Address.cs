using System.Text.RegularExpressions;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Customers;

/// <summary>
/// A customer's postal address in parts (§7.3), kept to fill hire agreements later. No part is required; every part is
/// trimmed and a blank part is empty. Australian addresses have a state from the fixed list and a 4-digit postcode.
/// </summary>
public sealed partial record Address
{
    public const int MaxLineLength = 100;
    public const int MaxLocalityLength = 60;
    public const int MaxStateLength = 60;
    public const int MaxPostcodeLength = 10;

    public static readonly IReadOnlyList<string> AustralianStates = ["NSW", "VIC", "QLD", "SA", "WA", "TAS", "NT", "ACT"];

    private Address(string? line1, string? line2, string? locality, string? state, string? postcode, string? country)
    {
        Line1 = line1;
        Line2 = line2;
        Locality = locality;
        State = state;
        Postcode = postcode;
        Country = country;
    }

    public string? Line1 { get; }

    public string? Line2 { get; }

    /// <summary>City/Locality.</summary>
    public string? Locality { get; }

    public string? State { get; }

    public string? Postcode { get; }

    /// <summary>ISO 3166-1 alpha-2, e.g. <c>AU</c>.</summary>
    public string? Country { get; }

    /// <summary>
    /// Validates and normalises the parts. Returns null for no address: every part blank, or only a country. Errors are
    /// reported per part (<c>address.line1</c> … <c>address.country</c>).
    /// </summary>
    public static Address? Create(string? line1, string? line2, string? locality, string? state, string? postcode, string? country)
    {
        var errors = new ValidationErrors();
        var l1 = Part(line1, MaxLineLength, "address.line1", "Address line 1", errors);
        var l2 = Part(line2, MaxLineLength, "address.line2", "Address line 2", errors);
        var city = Part(locality, MaxLocalityLength, "address.locality", "City/Locality", errors);
        var st = Part(state, MaxStateLength, "address.state", "State", errors);
        var pc = Part(postcode, MaxPostcodeLength, "address.postcode", "Postcode", errors);
        var cc = Blank(country) ? null : country!.Trim().ToUpperInvariant();
        if (cc is not null && !IsoCountryCodes.All.Contains(cc))
        {
            errors.Add("address.country", "Choose a country from the list.");
        }

        if (cc == IsoCountryCodes.Australia)
        {
            st = st?.ToUpperInvariant();
            if (st is not null && !AustralianStates.Contains(st))
            {
                errors.Add("address.state", "Choose an Australian state or territory.");
            }

            if (pc is not null && !FourDigits().IsMatch(pc))
            {
                errors.Add("address.postcode", "Enter a 4-digit postcode.");
            }
        }

        errors.ThrowIfAny();
        return l1 is null && l2 is null && city is null && st is null && pc is null
            ? null
            : new Address(l1, l2, city, st, pc, cc);
    }

    /// <summary>Rebuilds a stored address without validating it again.</summary>
    public static Address? Rehydrate(string? line1, string? line2, string? locality, string? state, string? postcode, string? country) =>
        line1 is null && line2 is null && locality is null && state is null && postcode is null
            ? null
            : new Address(line1, line2, locality, state, postcode, country);

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

    private static string? Part(string? value, int max, string field, string label, ValidationErrors errors)
    {
        var trimmed = Blank(value) ? null : value!.Trim();
        if (trimmed?.Length > max)
        {
            errors.Add(field, $"{label} must be {max} characters or fewer.");
        }

        return trimmed;
    }

    [GeneratedRegex("^[0-9]{4}$")]
    private static partial Regex FourDigits();
}

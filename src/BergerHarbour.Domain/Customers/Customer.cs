using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Customers;

public sealed class Customer : AggregateRoot<CustomerId>
{
    private Customer(CustomerId id, int version, string fullName, EmailAddress email, string? mobileNumber,
        string? secondaryPhone, DateTimeOffset createdDate, DateTimeOffset modifiedDate)
        : base(id, version)
    {
        FullName = fullName;
        Email = email;
        MobileNumber = mobileNumber;
        SecondaryPhone = secondaryPhone;
        CreatedDate = createdDate;
        ModifiedDate = modifiedDate;
    }

    public string FullName { get; private set; }

    public EmailAddress Email { get; }

    /// <summary>E.164. Required for online bookings, optional for staff-created ones.</summary>
    public string? MobileNumber { get; private set; }

    /// <summary>E.164, staff-entered only; empty when the customer is created.</summary>
    public string? SecondaryPhone { get; private set; }

    public DateTimeOffset CreatedDate { get; }

    public DateTimeOffset ModifiedDate { get; private set; }

    public static Customer Create(string fullName, string email, string? mobileNumber, DateTimeOffset now) =>
        new(CustomerId.New(), 0, Guard.Required(fullName, "fullName", "Full name"), EmailAddress.Create(email),
            NormaliseMobile(mobileNumber), null, now, now);

    /// <summary>
    /// Called when an existing customer books again: the name and (when given) mobile are updated to the latest
    /// values. Returns true when anything changed.
    /// </summary>
    public bool UpdateContactDetails(string fullName, string? mobileNumber, DateTimeOffset now)
    {
        var name = Guard.Required(fullName, "fullName", "Full name");
        var mobile = NormaliseMobile(mobileNumber) ?? MobileNumber;
        if (name == FullName && mobile == MobileNumber)
        {
            return false;
        }

        FullName = name;
        MobileNumber = mobile;
        ModifiedDate = now;
        return true;
    }

    /// <summary>Staff set or clear the secondary phone from the booking dialog. Returns true when it changed.</summary>
    public bool ChangeSecondaryPhone(string? phone, DateTimeOffset now)
    {
        var normalised = PhoneNumber.NormaliseOptional(phone, "secondaryPhone", "Secondary phone");
        if (normalised == SecondaryPhone)
        {
            return false;
        }

        SecondaryPhone = normalised;
        ModifiedDate = now;
        return true;
    }

    public CustomerSnapshot ToSnapshot() =>
        new(Id.Value, Version, FullName, Email.Value, MobileNumber, SecondaryPhone, CreatedDate, ModifiedDate);

    public static Customer FromSnapshot(CustomerSnapshot s) =>
        new(new CustomerId(s.Id), s.Version, s.FullName, EmailAddress.Create(s.Email), s.MobileNumber, s.SecondaryPhone,
            s.CreatedDate, s.ModifiedDate);

    private static string? NormaliseMobile(string? mobile) => PhoneNumber.NormaliseOptional(mobile, "mobile", "Mobile number");
}

public sealed record CustomerSnapshot(
    string Id,
    int Version,
    string FullName,
    string Email,
    string? MobileNumber,
    string? SecondaryPhone,
    DateTimeOffset CreatedDate,
    DateTimeOffset ModifiedDate);

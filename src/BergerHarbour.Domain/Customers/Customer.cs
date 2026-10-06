using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Customers;

public sealed class Customer : AggregateRoot<CustomerId>
{
    private Customer(CustomerId id, int version, string fullName, EmailAddress email, string? mobileNumber,
        DateTimeOffset createdDate, DateTimeOffset modifiedDate)
        : base(id, version)
    {
        FullName = fullName;
        Email = email;
        MobileNumber = mobileNumber;
        CreatedDate = createdDate;
        ModifiedDate = modifiedDate;
    }

    public string FullName { get; private set; }

    public EmailAddress Email { get; }

    public string? MobileNumber { get; private set; }

    public DateTimeOffset CreatedDate { get; }

    public DateTimeOffset ModifiedDate { get; private set; }

    public static Customer Create(string fullName, string email, string? mobileNumber, DateTimeOffset now) =>
        new(CustomerId.New(), 0, Guard.Required(fullName, "fullName", "Full name"), EmailAddress.Create(email),
            NormaliseMobile(mobileNumber), now, now);

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

    public CustomerSnapshot ToSnapshot() =>
        new(Id.Value, Version, FullName, Email.Value, MobileNumber, CreatedDate, ModifiedDate);

    public static Customer FromSnapshot(CustomerSnapshot s) =>
        new(new CustomerId(s.Id), s.Version, s.FullName, EmailAddress.Create(s.Email), s.MobileNumber, s.CreatedDate,
            s.ModifiedDate);

    private static string? NormaliseMobile(string? mobile) => Guard.Optional(mobile, "mobile", "Mobile number", 30);
}

public sealed record CustomerSnapshot(
    string Id,
    int Version,
    string FullName,
    string Email,
    string? MobileNumber,
    DateTimeOffset CreatedDate,
    DateTimeOffset ModifiedDate);

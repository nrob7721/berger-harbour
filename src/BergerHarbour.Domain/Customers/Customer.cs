using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Customers;

/// <summary>A customer, unique by normalised email. Created only by staff bookings or when an online deposit is paid.</summary>
public sealed class Customer : AggregateRoot
{
    public const int MaxAddressLength = 500;
    public const int MaxCommentsLength = 5000;

    private Customer(CustomerId id, string firstName, string lastName, EmailAddress email, DateTimeOffset createdDate)
    {
        Id = id;
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        CreatedDate = createdDate;
        ModifiedDate = createdDate;
    }

    public CustomerId Id { get; }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    public EmailAddress Email { get; private set; }

    public MobileNumber? MobileNumber { get; private set; }

    public MobileNumber? SecondaryMobileNumber { get; private set; }

    public string? Address { get; private set; }

    public string? Comments { get; private set; }

    public DateTimeOffset CreatedDate { get; }

    public DateTimeOffset ModifiedDate { get; private set; }

    public string FullName => $"{FirstName} {LastName}";

    /// <summary>A new customer. The secondary mobile and address start empty.</summary>
    public static Customer Create(CustomerId id, string? firstName, string? lastName, EmailAddress email, MobileNumber? mobile, DateTimeOffset now)
    {
        var customer = new Customer(id, RequiredName(firstName, "firstName", "First name is required."),
            RequiredName(lastName, "lastName", "Last name is required."), email, now)
        {
            MobileNumber = mobile,
        };
        return customer;
    }

    public static Customer Rehydrate(
        CustomerId id, string firstName, string lastName, EmailAddress email, MobileNumber? mobile, MobileNumber? secondaryMobile,
        string? address, string? comments, DateTimeOffset createdDate, DateTimeOffset modifiedDate, int version)
    {
        var customer = new Customer(id, firstName, lastName, email, createdDate)
        {
            MobileNumber = mobile,
            SecondaryMobileNumber = secondaryMobile,
            Address = address,
            Comments = comments,
            ModifiedDate = modifiedDate,
        };
        customer.SetPersistedVersion(version);
        return customer;
    }

    /// <summary>Trimmed, empty when blank, at most 500 characters.</summary>
    public static string? NormaliseAddress(string? address)
    {
        var trimmed = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        return trimmed?.Length > MaxAddressLength
            ? throw new DomainValidationException("address", "Address must be 500 characters or fewer.")
            : trimmed;
    }

    /// <summary>The same email booked again: the name and mobile take the latest values.</summary>
    public void UpdateFromBooking(string? firstName, string? lastName, MobileNumber? mobile, DateTimeOffset now)
    {
        FirstName = RequiredName(firstName, "firstName", "First name is required.");
        LastName = RequiredName(lastName, "lastName", "Last name is required.");
        if (mobile is not null)
        {
            MobileNumber = mobile;
        }

        ModifiedDate = now;
    }

    /// <summary>Staff edit every contact detail from the booking dialog.</summary>
    public void UpdateByStaff(
        string? firstName, string? lastName, EmailAddress email, MobileNumber? mobile, MobileNumber? secondaryMobile, string? address,
        string? comments, DateTimeOffset now)
    {
        var errors = new ValidationErrors();
        var first = errors.Capture(() => RequiredName(firstName, "firstName", "First name is required."));
        var last = errors.Capture(() => RequiredName(lastName, "lastName", "Last name is required."));
        var normalisedAddress = errors.Capture(() => NormaliseAddress(address));
        var normalisedComments = errors.Capture(() => NormaliseComments(comments, "customerComments"));
        errors.ThrowIfAny();
        FirstName = first!;
        LastName = last!;
        Email = email;
        MobileNumber = mobile;
        SecondaryMobileNumber = secondaryMobile;
        Address = normalisedAddress;
        Comments = normalisedComments;
        ModifiedDate = now;
    }

    public void UpdateComments(string? comments, DateTimeOffset now)
    {
        Comments = NormaliseComments(comments, "comments");
        ModifiedDate = now;
    }

    private static string? NormaliseComments(string? comments, string field)
    {
        var trimmed = string.IsNullOrWhiteSpace(comments) ? null : comments.Trim();
        return trimmed?.Length > MaxCommentsLength
            ? throw new DomainValidationException(field, "Comments must be 5000 characters or fewer.")
            : trimmed;
    }

    private static string RequiredName(string? name, string field, string message)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new DomainValidationException(field, message);
        }

        return trimmed.Length <= 100 ? trimmed : throw new DomainValidationException(field, "Names must be 100 characters or fewer.");
    }
}

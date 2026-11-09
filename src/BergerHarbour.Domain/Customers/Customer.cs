using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Customers;

public sealed class Customer : AggregateRoot
{
    public string Id { get; private set; } = "";
    public string FirstName { get; private set; } = "";
    public string LastName { get; private set; } = "";
    /// <summary>Normalised (lower-case, trimmed) and unique.</summary>
    public string Email { get; private set; } = "";
    public string? MobileNumber { get; private set; }
    public string? SecondaryMobileNumber { get; private set; }
    /// <summary>Staff-only notes shared by all of the customer's bookings.</summary>
    public string? Comments { get; private set; }

    public string FullName => $"{FirstName} {LastName}";

    private Customer() { }

    public static Customer Create(string firstName, string lastName, string email, string? mobile, DateTimeOffset now)
    {
        var errors = new ValidationErrors();
        var c = new Customer { Id = Ids.New() };
        var e = errors.Capture(() => EmailAddress.Require(email));
        var first = errors.Capture(() => Text.Required(firstName, "firstName", "First name", 100));
        var last = errors.Capture(() => Text.Required(lastName, "lastName", "Last name", 100));
        var m = errors.Capture(() => Shared.MobileNumber.Optional(mobile, "mobile"));
        errors.ThrowIfAny();
        c.Email = e!;
        c.FirstName = first!;
        c.LastName = last!;
        c.MobileNumber = m;
        c.InitialiseAudit(now);
        return c;
    }

    public static Customer Rehydrate(string id, string firstName, string lastName, string email, string? mobile, string? secondaryMobile,
        string? comments, int version, DateTimeOffset created, DateTimeOffset modified)
    {
        var c = new Customer
        {
            Id = id, FirstName = firstName, LastName = lastName, Email = email, MobileNumber = mobile,
            SecondaryMobileNumber = secondaryMobile, Comments = comments,
        };
        c.RestoreAudit(version, created, modified);
        return c;
    }

    /// <summary>An existing customer booked again online: names and mobile take the latest values. Secondary mobile and comments are never touched.</summary>
    public void UpdateFromOnlineBooking(string firstName, string lastName, string mobile)
    {
        var errors = new ValidationErrors();
        var first = errors.Capture(() => Text.Required(firstName, "firstName", "First name", 100));
        var last = errors.Capture(() => Text.Required(lastName, "lastName", "Last name", 100));
        var m = errors.Capture(() => Shared.MobileNumber.Require(mobile, "mobile"));
        errors.ThrowIfAny();
        FirstName = first!;
        LastName = last!;
        MobileNumber = m;
    }

    /// <summary>Staff edit contact details from the booking dialog.</summary>
    public void UpdateContactDetails(string firstName, string lastName, string? mobile, string? secondaryMobile)
    {
        var errors = new ValidationErrors();
        var first = errors.Capture(() => Text.Required(firstName, "firstName", "First name", 100));
        var last = errors.Capture(() => Text.Required(lastName, "lastName", "Last name", 100));
        var m = errors.Capture(() => Shared.MobileNumber.Optional(mobile, "mobile"));
        var s = errors.Capture(() => Shared.MobileNumber.Optional(secondaryMobile, "secondaryMobile"));
        errors.ThrowIfAny();
        FirstName = first!;
        LastName = last!;
        MobileNumber = m;
        SecondaryMobileNumber = s;
    }

    public void UpdateComments(string? comments) =>
        Comments = Text.Optional(comments, "customerComments", "Customer comments", 4000);
}

public interface ICustomerRepository
{
    Task<IReadOnlyList<Customer>> ListAsync(CancellationToken ct = default);
    Task<Customer?> GetAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<Customer>> GetManyAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default);
    Task<Customer?> GetByEmailAsync(string normalisedEmail, CancellationToken ct = default);
    /// <summary>Saves the customer and its unique email index together. Throws <see cref="ConflictException"/> when another customer owns the email.</summary>
    Task SaveAsync(Customer customer, CancellationToken ct = default);
}

using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

[FirestoreData]
public sealed class CustomerDocument : IVersionedDocument
{
    [FirestoreProperty("firstName")]
    public string FirstName { get; set; } = string.Empty;

    [FirestoreProperty("lastName")]
    public string LastName { get; set; } = string.Empty;

    [FirestoreProperty("email")]
    public string Email { get; set; } = string.Empty;

    [FirestoreProperty("mobileNumber")]
    public string MobileNumber { get; set; } = string.Empty;

    [FirestoreProperty("address")]
    public AddressDocument? Address { get; set; }

    [FirestoreProperty("comments")]
    public string? Comments { get; set; }

    [FirestoreProperty("createdDate")]
    public Timestamp CreatedDate { get; set; }

    [FirestoreProperty("modifiedDate")]
    public Timestamp ModifiedDate { get; set; }

    [FirestoreProperty("version")]
    public int Version { get; set; }

    public static CustomerDocument From(Customer c) => new()
    {
        FirstName = c.FirstName,
        LastName = c.LastName,
        Email = c.Email.Value,
        MobileNumber = c.MobileNumber.Value,
        Address = AddressDocument.From(c.Address),
        Comments = c.Comments,
        CreatedDate = FirestoreValues.Instant(c.CreatedDate),
        ModifiedDate = FirestoreValues.Instant(c.ModifiedDate),
        Version = c.Version,
    };

    public Customer ToDomain(string id) => Customer.Rehydrate(
        new CustomerId(id), FirstName, LastName, EmailAddress.Create(Email), Domain.Shared.MobileNumber.Create(MobileNumber), Address?.ToDomain(), Comments,
        FirestoreValues.Instant(CreatedDate), FirestoreValues.Instant(ModifiedDate), Version);
}

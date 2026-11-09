using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

[FirestoreData]
public sealed class CustomerDetailsDocument
{
    [FirestoreProperty("firstName")]
    public string FirstName { get; set; } = string.Empty;

    [FirestoreProperty("lastName")]
    public string LastName { get; set; } = string.Empty;

    [FirestoreProperty("email")]
    public string Email { get; set; } = string.Empty;

    [FirestoreProperty("mobileNumber")]
    public string MobileNumber { get; set; } = string.Empty;

    public static CustomerDetailsDocument From(CustomerDetails d) =>
        new() { FirstName = d.FirstName, LastName = d.LastName, Email = d.Email.Value, MobileNumber = d.MobileNumber.Value };

    public CustomerDetails ToDomain() => new(FirstName, LastName, EmailAddress.Create(Email), Domain.Shared.MobileNumber.Create(MobileNumber));
}

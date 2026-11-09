namespace BergerHarbour.Domain.Customers;

/// <summary>Another customer already has this email.</summary>
public sealed class DuplicateCustomerEmailException : Exception
{
    public DuplicateCustomerEmailException(string email)
        : base($"A customer with the email {email} already exists.")
    {
        Email = email;
    }

    public string Email { get; }
}

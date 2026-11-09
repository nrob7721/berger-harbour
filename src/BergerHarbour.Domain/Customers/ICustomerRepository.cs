using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Customers;

public interface ICustomerRepository
{
    Task<IReadOnlyList<Customer>> ListAsync(CancellationToken ct = default);

    Task<Customer?> GetAsync(CustomerId id, CancellationToken ct = default);

    Task<Customer?> GetByEmailAsync(EmailAddress email, CancellationToken ct = default);

    /// <summary>
    /// Saves the customer and keeps the email unique: throws <see cref="DuplicateCustomerEmailException"/> if another
    /// customer already has its email (checked atomically with an email index).
    /// </summary>
    Task SaveAsync(Customer customer, CancellationToken ct = default);
}

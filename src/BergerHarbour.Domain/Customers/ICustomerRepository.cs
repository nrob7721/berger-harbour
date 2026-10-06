using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Customers;

public interface ICustomerRepository
{
    Task<Customer?> GetAsync(CustomerId id, CancellationToken ct = default);

    Task<Customer?> GetByEmailAsync(EmailAddress email, CancellationToken ct = default);

    Task<IReadOnlyList<Customer>> GetManyAsync(IEnumerable<CustomerId> ids, CancellationToken ct = default);

    Task<IReadOnlyList<Customer>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Inserts a new customer (Version 0) or updates an existing one with an optimistic version check.
    /// Throws <see cref="DuplicateCustomerEmailException"/> when a new customer's email is already taken.
    /// </summary>
    Task SaveAsync(Customer customer, CancellationToken ct = default);
}

public sealed class DuplicateCustomerEmailException(string email)
    : DomainValidationException("email", $"A customer with email {email} already exists.");

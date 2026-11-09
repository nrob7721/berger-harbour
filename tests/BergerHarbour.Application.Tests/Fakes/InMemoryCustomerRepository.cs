using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Tests.Fakes;

public sealed class InMemoryCustomerRepository(InMemoryDatabase db) : ICustomerRepository
{
    public Task<IReadOnlyList<Customer>> ListAsync(CancellationToken ct = default) => Task.FromResult(db.Customers.All);

    public Task<Customer?> GetAsync(CustomerId id, CancellationToken ct = default) => Task.FromResult(db.Customers.Get(id));

    public Task<Customer?> GetByEmailAsync(EmailAddress email, CancellationToken ct = default) =>
        Task.FromResult(db.Customers.All.FirstOrDefault(c => c.Email == email));

    public Task SaveAsync(Customer customer, CancellationToken ct = default)
    {
        lock (db.Customers)
        {
            if (db.Customers.All.Any(c => c.Email == customer.Email && c.Id != customer.Id))
            {
                throw new DuplicateCustomerEmailException(customer.Email.Value);
            }

            db.Customers.Save(customer);
        }

        return Task.CompletedTask;
    }
}

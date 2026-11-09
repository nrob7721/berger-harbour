using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Infrastructure.IntegrationTests.Support;

namespace BergerHarbour.Infrastructure.IntegrationTests.Firestore.Repositories;

[Collection(EmulatorCollection.Name)]
public class FirestoreCustomerRepositoryTests(EmulatorFixture fx)
{
    private ICustomerRepository Repository => fx.Get<ICustomerRepository>();

    [Fact]
    public async Task A_customer_round_trips_and_is_found_by_email()
    {
        var customer = Customer.Create(CustomerId.New(), "Jane", "Smith", EmailAddress.Create("round@example.com"), MobileNumber.Create("0412345678"),
            fx.Clock.UtcNow);
        customer.UpdateByStaff("Jane", "Smith", customer.Email, customer.MobileNumber, MobileNumber.Create("0412000111"), "1 River Rd", "VIP", fx.Clock.UtcNow);
        await Repository.SaveAsync(customer);
        var loaded = (await Repository.GetByEmailAsync(EmailAddress.Create("ROUND@example.com")))!;
        Assert.Equal(customer.Id, loaded.Id);
        Assert.Equal("+61412000111", loaded.SecondaryMobileNumber!.Value);
        Assert.Equal("1 River Rd", loaded.Address);
        Assert.Equal("VIP", loaded.Comments);
    }

    [Fact]
    public async Task Email_uniqueness_holds_under_concurrency()
    {
        var email = EmailAddress.Create($"race-{Guid.NewGuid():N}@example.com");
        var attempts = Enumerable.Range(0, 2).Select(async _ =>
        {
            try
            {
                await Repository.SaveAsync(Customer.Create(CustomerId.New(), "Jane", "Smith", email, null, fx.Clock.UtcNow));
                return true;
            }
            catch (DuplicateCustomerEmailException)
            {
                return false;
            }
        });
        var results = await Task.WhenAll(attempts);
        Assert.Equal(1, results.Count(r => r));
        Assert.NotNull(await Repository.GetByEmailAsync(email));
    }

    [Fact]
    public async Task Changing_the_email_moves_the_index()
    {
        var customer = Customer.Create(CustomerId.New(), "Jane", "Smith", EmailAddress.Create("old@example.com"), null, fx.Clock.UtcNow);
        await Repository.SaveAsync(customer);
        customer.UpdateByStaff("Jane", "Smith", EmailAddress.Create("new@example.com"), null, null, null, null, fx.Clock.UtcNow);
        await Repository.SaveAsync(customer);
        Assert.Null(await Repository.GetByEmailAsync(EmailAddress.Create("old@example.com")));
        Assert.Equal(customer.Id, (await Repository.GetByEmailAsync(EmailAddress.Create("new@example.com")))!.Id);
    }
}

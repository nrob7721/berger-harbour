using BergerHarbour.Application.Abstractions;

namespace BergerHarbour.Application.Tests.Fakes;

public sealed class FakeBookingReferenceGenerator : IBookingReferenceGenerator
{
    public Dictionary<string, int> Counters { get; } = [];

    public Task<int> NextSuffixAsync(string prefix, CancellationToken ct = default)
    {
        lock (Counters)
        {
            var next = Counters.GetValueOrDefault(prefix);
            Counters[prefix] = next + 1;
            return Task.FromResult(next);
        }
    }
}

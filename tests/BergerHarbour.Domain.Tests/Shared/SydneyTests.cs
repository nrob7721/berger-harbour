using BergerHarbour.Domain.Shared;
using static BergerHarbour.Domain.Tests.Support.TestDates;

namespace BergerHarbour.Domain.Tests.Shared;

public class SydneyTests
{
    [Theory]
    [InlineData("2026-10-16", "AEDT/NSW")]
    [InlineData("2026-07-16", "AEST/NSW")]
    [InlineData("2027-04-04", "AEST/NSW")]
    [InlineData("2026-10-04", "AEDT/NSW")]
    public void Zone_label_is_whichever_applies_on_that_date(string date, string label) => Assert.Equal(label, Sydney.ZoneLabel(D(date)));

    [Fact]
    public void Today_is_the_sydney_date() => Assert.Equal(D("2026-11-10"), Sydney.DateOf(At("2026-11-09T14:00:00Z")));

    [Fact]
    public void Check_in_instant_is_11am_sydney() => Assert.Equal(At("2026-10-16T00:00:00Z"), Sydney.InstantOf(D("2026-10-16"), new TimeOnly(11, 0)));
}
